-- From LuAshitacast 2.14 by ThornyFFXI (https://github.com/ThornyFFXI/LuAshitacast), MIT licence,
-- Copyright (c) 2021 ThornyFFXI; see LICENSE-LuAshitacast. Lines marked "vanagear:" are Vanadreams changes.
local state = {
    ActionDelay = 0,
    CurrentCall = 'N/A',
    DelayedEquip = {},
    ForceSet = nil,
    ForceSetTimer = 0,
    Injecting = false,
    PetAction = nil,
    PlayerAction = nil,
    PlayerId = 0,
    PlayerJob = 0,
    PlayerName = '',
    pWardrobe = 0,
    pZoneFlags = 0,
    pZoneOffset = 0
};

state.Init = function()
    gState.pVanaTime = ashita.memory.find('FFXiMain.dll', 0, 'B0015EC390518B4C24088D4424005068', 0, 0);
    gState.pWardrobe = ashita.memory.find('FFXiMain.dll', 0, 'A1????????8B88B4000000C1E907F6C101E9', 1, 0);
    gState.pWeather = ashita.memory.find('FFXiMain.dll', 0, '66A1????????663D????72', 0, 0);
    gState.pZoneFlags = ashita.memory.find('FFXiMain.dll', 0, '8B8C24040100008B90????????0BD18990????????8B15????????8B82', 0, 0);

    if (gState.pVanaTime == 0) then
        print(chat.header('vanagear') .. chat.error('Vanatime signature scan failed.'));
    end

    if (gState.pWardrobe == 0) then
        print(chat.header('vanagear') .. chat.error('Wardrobe access signature scan failed.'));
    end

    if (gState.pWeather == 0) then
        print(chat.header('vanagear') .. chat.error('Weather signature scan failed.'));
    end

    if (gState.pZoneFlags == 0) then
        print(chat.header('vanagear') .. chat.error('Zone flag signature scan failed.'));
    else
        gState.pZoneOffset = ashita.memory.read_uint32(gState.pZoneFlags, 0x09);
        if (gState.pZoneOffset == 0) then
            gState.pZoneFlags = 0;
            print(chat.header('vanagear') .. chat.error('Zone flag offset not found.'));
        else
            gState.pZoneFlags = ashita.memory.read_uint32(gState.pZoneFlags, 0x17);
            if (gState.pZoneFlags == 0) then
                print(chat.header('vanagear') .. chat.error('Zone flag sub pointer not found.'));
            end
        end
    end

    state.Disabled = {};
    state.Encumbrance = {};
    for i = 1,16,1 do
        state.Disabled[i] = false;
        state.Encumbrance[i] = false;
    end
    
    -- vanagear: a profile's require and gFunc.LoadFile look in vanagear's profiles folder, never luashitacast's
    local configPath = string.format('%sconfig\\addons\\vanagear\\profiles\\?.lua;', AshitaCore:GetInstallPath());
    package.path = configPath .. package.path;
    gState.BasePath = package.path;
    
    if (AshitaCore:GetMemoryManager():GetParty():GetMemberIsActive(0) == 1) then
        gState.PlayerId = AshitaCore:GetMemoryManager():GetParty():GetMemberServerId(0);
        gState.PlayerName = AshitaCore:GetMemoryManager():GetParty():GetMemberName(0);
        gState.PlayerJob = AshitaCore:GetMemoryManager():GetPlayer():GetMainJob();
        configPath = string.format('%sconfig\\addons\\vanagear\\profiles\\%s_%u\\?.lua;', AshitaCore:GetInstallPath(), gState.PlayerName, gState.PlayerId);
        package.path = configPath .. gState.BasePath;
        -- vanagear: loaded after the zone wait, never in the same instant as everything else
        gState.PendingAutoLoad = true;
    end
end

state.ResetSettings = function(currentSettings)
    gSettings = {};
    for k,v in pairs(gDefaultSettings) do
        if (type(v) == 'table') then
            gSettings[k] = {};
            for subK,subV in pairs(v) do
                gSettings[k][subK] = subV;
            end
        else
            gSettings[k] = v;
        end
    end
    for k,v in pairs(gActiveSettings) do
        if (type(currentSettings) == 'table') and (currentSettings[k] ~= nil) then
            gSettings[k] = currentSettings[k];
        else
            gSettings[k] = v;
        end
    end
end

state.LoadProfile = function(profilePath)
    local shortFileName = profilePath:match("[^\\]*.$");
    local success, loadError = loadfile(profilePath);
    if not success then
        gProfile = nil;
        print(chat.header('vanagear') .. chat.error('Failed to load profile: ') .. chat.color1(2, shortFileName));
        print(chat.header('vanagear') .. chat.error(loadError));
        return;
	end
	gProfile = success();
	if (gProfile ~= nil) then
        -- vanagear: a profile the launcher made from GearSwap gets the built sets under it
        local f = io.open(profilePath, 'r');
        if (f ~= nil) then
            local head = f:read(400) or '';
            f:close();
            if (string.find(head, 'imported from GearSwap by the Vanadreams launcher', 1, true)) then
                gProfile.ImportedFromGearSwap = true;
            end
        end
        state.ResetSettings();
        print(chat.header('vanagear') .. chat.message('Loaded profile: ') .. chat.color1(2, shortFileName));
        -- vanagear: every profile knows its file, OnLoad or not (/vg addset, /vg reload, the writers in vg\profile.lua)
        gProfile.FilePath = profilePath;
        gProfile.FileName = shortFileName;
        gProfile.VgJob = string.match(shortFileName, '^(.-)%.lua$') or shortFileName;
        if (gProfile.OnLoad ~= nil) and (type(gProfile.OnLoad) == 'function') then
            gState.SafeCall('OnLoad');
            gSetDisplay:Reload();
        end
        -- vanagear: a fresh build goes into the profile's built table, then its sets are read back merged
        if (gVanaCore ~= nil) then
            gVanaCore.ApplyOwn();
            gVanaCore.ApplyLocks(gProfile.VgJob);   -- the job's saved slot locks (/vg lock, /vg disable)
        end
    end
end

state.AutoLoadProfile = function()
    gState.UnloadProfile();
    
    local jobString = AshitaCore:GetResourceManager():GetString("jobs.names_abbr", gState.PlayerJob);
    if type(jobString) == 'string'then
        jobString = jobString:trimend('\x00');
    end
    -- vanagear: the job's profile is vanagear's own, config\addons\vanagear\profiles\<Name>_<id>\<JOB>.lua
    -- (vg\profile.lua). With none there yet, the player's LuAshitacast profile for the job is pulled over
    -- whole, the one time the luashitacast folder is read; with none of those either, vanagear's template.
    -- A DefaultProfile setting names a file in vanagear's profiles folder.
    local vgProfile = require('vg.profile');
    local job = tostring(jobString);
    local path = vgProfile.Path(job);
    if (not ashita.fs.exists(path)) and (gSettings.DefaultProfile ~= nil) then
        local default = ('%s\\%s'):fmt(vgProfile.Root(), gSettings.DefaultProfile);
        if (ashita.fs.exists(default)) then
            gState.LoadProfile(default);
            return;
        end
    end
    local lacPath = nil;
    for _,lac in ipairs({
        ('%sconfig\\addons\\luashitacast\\%s_%u\\%s.lua'):fmt(AshitaCore:GetInstallPath(), gState.PlayerName, gState.PlayerId, job),
        ('%sconfig\\addons\\luashitacast\\%s_%s.lua'):fmt(AshitaCore:GetInstallPath(), gState.PlayerName, job),
    }) do
        if (not ashita.fs.exists(path)) and (ashita.fs.exists(lac)) then
            lacPath = lac;
            break;
        end
    end
    path = vgProfile.Ensure(job, lacPath);
    if (path ~= nil) then
        gState.LoadProfile(path);
    else
        print(chat.header('vanagear') .. chat.error('Could not make a profile for ' .. job .. ' in ' .. vgProfile.Folder() .. '.'));
    end
end

state.LoadProfileEx = function(path)
    gState.UnloadProfile();
    
    local profilePath = path;
    if (not ashita.fs.exists(profilePath)) then
        profilePath = path .. '.lua';
        if (not ashita.fs.exists(profilePath)) then
            -- vanagear: /vg load <name> looks in vanagear's profiles folder
            profilePath = ('%sconfig\\addons\\vanagear\\profiles\\%s_%u\\%s'):fmt(AshitaCore:GetInstallPath(), gState.PlayerName, gState.PlayerId, path);
            if (not ashita.fs.exists(profilePath)) then
                profilePath = ('%sconfig\\addons\\vanagear\\profiles\\%s_%u\\%s.lua'):fmt(AshitaCore:GetInstallPath(), gState.PlayerName, gState.PlayerId, path);
                if (not ashita.fs.exists(profilePath)) then
                    profilePath = ('%sconfig\\addons\\vanagear\\profiles\\%s'):fmt(AshitaCore:GetInstallPath(), path);
                    if (not ashita.fs.exists(profilePath)) then
                        profilePath = ('%sconfig\\addons\\vanagear\\profiles\\%s.lua'):fmt(AshitaCore:GetInstallPath(), path);
                        if (not ashita.fs.exists(profilePath)) then
                            print(chat.header('vanagear') .. chat.error('Profile not found matching: ') .. chat.color1(2, path));
                            return;
                        end
                    end
                end
            end
        end
    end

    gState.LoadProfile(profilePath);
end

state.UnloadProfile = function()
    if (gProfile ~= nil) then
        gSetDisplay:Hide();
        gState.SafeCall('OnUnload');
        coroutine.sleep(0.5);
    end
    gState.Reset();
end

local defaultParsed = false;
state.HandleEquipEvent = function(eventName, equipStyle)
    -- vanagear: another LuAshitacast is swapping too; two engines on every slot fight, so this one stays out
    if (gVanaCore.StandingDown()) then
        return;
    end
    if (gProfile ~= nil) then
        if (eventName == 'HandleDefault') then
            if (gSettings.HorizonMode) and (defaultParsed) then
                return;
            end
        end

        local event = gProfile[eventName];
        if (event ~= nil) and (type(event) == 'function') then
            gEquip.ClearBuffer();
            gState.CurrentCall = eventName;
            -- vanagear: the profile's lua is the only thing that swaps, as LuAshitacast runs it. Its sets
            -- are read from the file (profile.Refresh: your sets, with the file's built table filling empty
            -- slots when the underlay is on). vanagear's own rules (vg\stock.lua) no longer run beside it:
            -- two swap systems, one from memory and one from the lua, fought on every slot. Lee, 24 Oct 2026:
            -- "so now it tries to read the memory and the lua rather than jsut the lua"; "its called lua lua
            -- shitacast". A profile wanting vanagear's rules gets them written in with /vg template.
            gState.SafeCall(eventName);
            if (eventName ~= nil) then
                gVanaUse.Tick(eventName);
            end
            if (eventName == 'HandleDefault') then
                gEquip.ProcessBuffer(equipStyle);
                defaultParsed = true;
            elseif (gState.PlayerAction ~= nil) and (gState.PlayerAction.Block ~= true) then
                if (gState.DelayedEquip.Timer ~= nil) and ((eventName == 'HandleMidcast') or (eventName == 'HandleMidshot')) then
                    gState.DelayedEquip.Tag = eventName .. 'Delayed';
                    gEquip.ProcessImmediateBuffer(equipStyle);
                else
                    gEquip.ProcessBuffer(equipStyle);
                end
            end
            gState.CurrentCall = 'N/A';
        end
    end
end

state.Inject = function(id, data)
    gState.Injecting = true;
    AshitaCore:GetPacketManager():AddOutgoingPacket(id, data);
    gState.Injecting = false;
end

state.Reset = function()
    gProfile = nil;
    state.Disabled = {};
    state.Encumbrance = {};
    for i = 1,16,1 do
        state.Disabled[i] = false;
        state.Encumbrance[i] = false;
    end
    state.LockStyle = nil;
    state.DelayedEquip = {};
    gState.ForceSet = nil;
    
    --Iterate global table to look for any leftover globals that didn't exist before profile was loaded.
    local newGlobs = T{};
    for key,_ in pairs(_G) do
        if gPreservedGlobalKeys[key] == nil then
            newGlobs:append(key);
        end
    end

    --Get rid of all new globals..
    for _,glob in ipairs(newGlobs) do
        _G[glob] = nil;
    end

    state.ResetSettings();
end

state.SafeCall = function(name,...)
    if (gProfile ~= nil) then
        if (type(gProfile[name]) == 'function') then
            if (gSettings.SafeCall) then
                local success,err = pcall(gProfile[name],...);
                if (not success) then
                    print(chat.header('vanagear') .. chat.error('Error in profile function: ') .. chat.color1(2, name));
                    print(chat.header('vanagear') .. chat.error(err));
                end
            else
                gProfile[name](...);
            end
            defaultParsed = false;
        elseif (gProfile[name] ~= nil) then
            print(chat.header('vanagear') .. chat.error('Profile member exists but is not a function: ') .. chat.color1(2, name));
        end
    end
end

return state;