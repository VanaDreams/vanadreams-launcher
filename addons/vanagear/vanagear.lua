--[[
    vanagear: gear sets built from your own bags, for your job and level.

    Built on LuAshitacast by ThornyFFXI (https://github.com/ThornyFFXI/LuAshitacast, MIT licence,
    Copyright (c) 2021 ThornyFFXI; see LICENSE-LuAshitacast). LuAshitacast 2.14's files are
    carried here and still do the swapping; vanagear's own code is in vg\, the server's gear
    tables in data\ and the job weights in jobs\.
]]--

addon.name      = 'vanagear';
addon.author    = 'Vanadreams, on LuAshitacast by Thorny';
addon.version   = '0.2.3';
addon.desc      = 'Gear sets built from your own bags for your job and level, written into a profile for each job.';
addon.link      = 'https://github.com/VanaDreams/vanadreams-launcher/tree/main/addons/vanagear';

require('common');
chat = require('chat');
local jit = require('jit');
jit.off();
if (ashita.addons_version >= 4.3) then
    addon.instance:enable_feature(addon_feature.use_packet_chunks);
end

gConfigGUI           = require('config');
gData                = require('data');
gFunc                = require('func');
gEquip               = require('equip');
gFileTools           = require('filetools');
gIntegration         = require('integration');
gState               = require('state');
gCommandHandlers     = require('commandhandlers');
gPacketHandlers      = require('packethandlers');
gSetDisplay          = require('setdisplay');
gPreservedGlobalKeys = T{};

-- vanagear's own parts; made before the load event so LuAshitacast keeps them across profiles
gVanaCore            = require('vg.core');
gVanaStock           = require('vg.stock');
gVanaInclude         = require('vg.include');   -- the job templates' shared checks (local vg = gVanaInclude)
gVanaUse             = require('vg.use');
gVanaMine            = require('vg.mine');
gVanaCommands        = require('vg.commands');
gVanaWindow          = require('vg.window');

-- Ashita unloads an addon when one of its event callbacks throws. vanagear catches its own
-- errors instead: each part runs guarded, the error is printed once (with where it came from)
-- and vanagear keeps running. A repeat of the same error stays quiet so chat isn't flooded.
local reported = {};
local function guard(part, fn, ...)
    local ok, err = xpcall(fn, debug.traceback, ...);
    if (ok) then
        return true;
    end
    err = tostring(err);
    local first = err:match('^[^\n]*') or err;
    if (reported[first] == nil) then
        reported[first] = true;
        print(chat.header('vanagear') .. chat.error(part .. ': ' .. first));
        -- where it was called from, on one short line: the first frames past the error itself
        local frames = {};
        for line in err:gmatch('\n%s*([^\n]+)') do
            if (#frames < 3) and (line:find('^%[C%]') == nil) and (line ~= 'stack traceback:') and (line:find("'guard'") == nil) then
                frames[#frames + 1] = line;
            end
        end
        if (#frames > 1) then
            print(chat.header('vanagear') .. chat.message('from ' .. table.concat(frames, ' < ', 2)));
        end
    end
    return false;
end
gVanaGuard = guard;

ashita.events.register('load', 'vanagear_load_cb', function ()
    --Create a list of all globals the ashita environment has created.
    --This will be used to clear all leftover globals when loading a new profile.
    for key,_ in pairs(_G) do
        gPreservedGlobalKeys[key] = true;
    end
    guard('load prefs', gVanaCore.LoadPrefs);
    guard('load', gState.Init);
end);

ashita.events.register('unload', 'vanagear_unload_cb', function ()
    guard('unload', gState.UnloadProfile);
end);

-- each part on its own, so one failing window doesn't stop the swaps or the other windows
ashita.events.register('d3d_present', 'vanagear_render', function()
    guard('tick', gVanaCore.Tick);
    guard('bag watch', gVanaMine.Tick);
    guard('config window', gConfigGUI.Render, gConfigGUI);
    guard('set display', gSetDisplay.Render, gSetDisplay);
    guard('vanagear window', gVanaWindow.Render);
end);

ashita.events.register('packet_in', 'vanagear_packet_in_cb', function (e)
    guard('incoming packet', gPacketHandlers.HandleIncomingPacket, e);
end);

ashita.events.register('packet_out', 'vanagear_packet_out_cb', function (e)
    guard('outgoing packet', gPacketHandlers.HandleOutgoingPacket, e);
end);

-- the server's answers about your trusts, for their tabs (hidden from chat)
ashita.events.register('text_in', 'vanagear_text_in_cb', function (e)
    -- the echo of the commands Vanagear sends for your trusts (Lee, 23 Oct 2026: keep them out of the chat box)
    local msg = e.message or '';
    if (string.find(msg, '!trustgear ', 1, true) ~= nil) or (string.find(msg, '!trustuse ', 1, true) ~= nil) then
        e.blocked = true;
        return;
    end
    if (string.find(msg, '[VG ', 1, true) ~= nil) then
        guard('trust info', require('vg.trusts').TextIn, e);
    end
end);

ashita.events.register('command', 'vanagear_command_cb', function (e)
    guard('command', function ()
        -- vanagear answers /vg (and /gs) only; the engine's commands are /vg commands (vg\commands.lua)
        gVanaCommands.Handle(e);
    end);
end);
