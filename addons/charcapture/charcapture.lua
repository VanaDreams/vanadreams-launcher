--[[
    charcapture - snapshot a character for porting to Vanadreams.

    /capture         write config\charcapture\<Name>.json and print a summary
    /capture show    print the last summary again

    Reads only through Ashita's memory managers. Sends nothing, changes nothing.
    Runs on retail or any server; the file records which.
]]

addon.name    = 'charcapture';
addon.author  = 'Vanadreams';
addon.version = '0.1.10';
addon.desc    = 'Captures a character snapshot for porting to Vanadreams.';
addon.link    = 'https://github.com/VanaDreams/vanadreams-launcher';

require('common');
local json = require('json');

local last_summary = nil;

local function hex(s)
    if s == nil then return ''; end
    return (s:gsub('.', function (c) return ('%02x'):format(c:byte()); end));
end

-- Spent merits and job point upgrades are not in Ashita's memory managers; the game only sends
-- them when the Merit Points and Job Points menus are opened. Those packets are remembered here
-- and go into the next snapshot.
--   0x08C: u16 count at 0x04, then count entries of { u16 merit id, u8 next cost, u8 upgrades }
--   0x08D: 64 entries of { u16 index:5 | job:11, u16 next:10 | level:6 } from 0x04
local seen = T{ chocobo = nil, chocobo_at = nil, merits = T{}, merits_at = nil, job_points = T{}, job_points_at = nil, logs = T{}, logs_at = nil, currencies = T{}, currencies1_at = nil, currencies2_at = nil, skills_by_job = T{}, skills_due = nil, automaton = nil, automaton_at = nil };

-- Quest and mission logs arrive as 0x056 packets when you zone: a 32-byte block at 0x04 and a
-- 'port' at 0x24 saying which log it is (current or completed quests per area, completed
-- missions, and 0xFFFF for the current missions). The blocks are kept raw, keyed by port; the
-- importer knows the layout because the server builds these same packets.
local function read_log(data)
    if #data < 0x28 then return; end
    local port = struct.unpack('<H', data, 0x25);
    if port == nil then return; end
    seen.logs[('%04x'):format(port)] = hex(data:sub(5, 36));
    seen.logs_at = os.time();
end

local function read_merits(data)
    local count = struct.unpack('<H', data, 0x05);
    if count == nil or count > 61 then return; end
    for i = 0, count - 1 do
        local id, _, upgrades = struct.unpack('<HBB', data, 0x09 + i * 4);
        if id and id > 0 and upgrades > 0 then seen.merits[tostring(id)] = upgrades; end
    end
    seen.merits_at = os.time();
end

local function read_job_points(data)
    for i = 0, 63 do
        local a, b = struct.unpack('<HH', data, 0x05 + i * 4);
        if a == nil or a == 0 then break; end
        local index, job = bit.band(a, 0x1F), bit.rshift(a, 5);
        local level = bit.rshift(b, 10);
        if job > 0 and level > 0 then
            seen.job_points[tostring(job)] = seen.job_points[tostring(job)] or T{};
            seen.job_points[tostring(job)][tostring(index)] = level;
        end
    end
    seen.job_points_at = os.time();
end

-- Your own chocobo (the one the Chocobo Whistle calls): its colour, beak, tail, talons, speed and riding
-- time are one u32 the game only sends while you ride it. It comes in 0x067 for you (type 0x02 at 0x04,
-- your id at 0x08, the u32 at 0x18) and in 0x00D for your character (your id at 0x04, ChocoboIndex 2 in
-- bits 5-7 of 0x20, the u32 at 0x34). Riding it once with charcapture loaded is enough.
local function own_id()
    return AshitaCore:GetMemoryManager():GetParty():GetMemberServerId(0);
end

local function read_chocobo_sync(data)
    if #data < 0x1C or data:byte(0x05) ~= 0x02 then return; end
    if struct.unpack('<I', data, 0x09) ~= own_id() then return; end
    local props = struct.unpack('<I', data, 0x19);
    if props and props ~= 0 then seen.chocobo = props; seen.chocobo_at = os.time(); end
end

local function read_chocobo_update(data)
    if #data < 0x38 or struct.unpack('<I', data, 0x05) ~= own_id() then return; end
    if bit.band(bit.rshift(data:byte(0x21), 5), 0x07) ~= 2 then return; end
    local props = struct.unpack('<I', data, 0x35);
    if props and props ~= 0 then seen.chocobo = props; seen.chocobo_at = os.time(); end
end

-- The Currencies tabs of the Profile menu. The game sends them when the tab is opened:
--   0x113: Currencies 1 (conquest, guild, assault and Limbus-era points, crystals stored, ...)
--   0x118: Currencies 2 (Bayld, stones, Hallmarks, Gallantry, ...)
-- Every number is read at the offset the server's own packet uses and kept under the name of the
-- Vanadreams database column it belongs to, so the importer can write it straight across. Offsets are
-- 0-based into the packet (4 byte header included); the formats are struct.unpack letters.
-- Generated from src/map/packets/s2c/0x113_currencies_1.h and 0x118_currencies_2.h.
local CURRENCY_1 = {
    { 4, 'i', 'sandoria_cp' }, { 8, 'i', 'bastok_cp' }, { 12, 'i', 'windurst_cp' }, { 16, 'H', 'beastman_seal' },
    { 18, 'H', 'kindred_seal' }, { 20, 'H', 'kindred_crest' }, { 22, 'H', 'high_kindred_crest' },
    { 24, 'H', 'sacred_kindred_crest' }, { 26, 'H', 'ancient_beastcoin' }, { 28, 'H', 'valor_point' },
    { 30, 'H', 'scyld' }, { 32, 'i', 'guild_fishing' }, { 36, 'i', 'guild_woodworking' },
    { 40, 'i', 'guild_smithing' }, { 44, 'i', 'guild_goldsmithing' }, { 48, 'i', 'guild_weaving' },
    { 52, 'i', 'guild_leathercraft' }, { 56, 'i', 'guild_bonecraft' }, { 60, 'i', 'guild_alchemy' },
    { 64, 'i', 'guild_cooking' }, { 68, 'i', 'cinder' }, { 72, 'B', 'fire_fewell' }, { 73, 'B', 'ice_fewell' },
    { 74, 'B', 'wind_fewell' }, { 75, 'B', 'earth_fewell' }, { 76, 'B', 'lightning_fewell' },
    { 77, 'B', 'water_fewell' }, { 78, 'B', 'light_fewell' }, { 79, 'B', 'dark_fewell' },
    { 80, 'i', 'ballista_point' }, { 84, 'i', 'fellow_point' }, { 88, 'H', 'chocobuck_sandoria' },
    { 90, 'H', 'chocobuck_bastok' }, { 92, 'H', 'chocobuck_windurst' }, { 96, 'i', 'research_mark' },
    { 100, 'B', 'tunnel_worm' }, { 101, 'B', 'morion_worm' }, { 102, 'B', 'phantom_worm' },
    { 104, 'i', 'moblin_marble' }, { 108, 'H', 'infamy' }, { 110, 'H', 'prestige' }, { 112, 'i', 'legion_point' },
    { 116, 'i', 'spark_of_eminence' }, { 120, 'i', 'shining_star' }, { 124, 'i', 'imperial_standing' },
    { 128, 'i', 'leujaoam_assault_point' }, { 132, 'i', 'mamool_assault_point' },
    { 136, 'i', 'lebros_assault_point' }, { 140, 'i', 'periqia_assault_point' }, { 144, 'i', 'ilrusi_assault_point' },
    { 148, 'i', 'nyzul_isle_assault_point' }, { 152, 'i', 'zeni_point' }, { 156, 'i', 'jetton' },
    { 160, 'i', 'therion_ichor' }, { 164, 'i', 'allied_notes' }, { 168, 'H', 'aman_vouchers' },
    { 170, 'H', 'login_points' }, { 172, 'i', 'cruor' }, { 176, 'i', 'resistance_credit' },
    { 180, 'i', 'dominion_note' }, { 184, 'B', 'fifth_echelon_trophy' }, { 185, 'B', 'fourth_echelon_trophy' },
    { 186, 'B', 'third_echelon_trophy' }, { 187, 'B', 'second_echelon_trophy' }, { 188, 'B', 'first_echelon_trophy' },
    { 189, 'B', 'cave_points' }, { 190, 'B', 'id_tags' }, { 191, 'B', 'op_credits' }, { 192, 'i', 'traverser_stones' },
    { 196, 'i', 'voidstones' },
    { 200, 'i', 'kupofried_corundums' }, { 204, 'B', 'pheromone_sacks' }, { 206, 'B', 'rems_ch1' },
    { 207, 'B', 'rems_ch2' }, { 208, 'B', 'rems_ch3' }, { 209, 'B', 'rems_ch4' }, { 210, 'B', 'rems_ch5' },
    { 211, 'B', 'rems_ch6' }, { 212, 'B', 'rems_ch7' }, { 213, 'B', 'rems_ch8' }, { 214, 'B', 'rems_ch9' },
    { 215, 'B', 'rems_ch10' }, { 224, 'H', 'reclamation_marks' }, { 228, 'i', 'unity_accolades' },
    { 232, 'H', 'fire_crystals' }, { 234, 'H', 'ice_crystals' }, { 236, 'H', 'wind_crystals' },
    { 238, 'H', 'earth_crystals' }, { 240, 'H', 'lightning_crystals' }, { 242, 'H', 'water_crystals' },
    { 244, 'H', 'light_crystals' }, { 246, 'H', 'dark_crystals' }, { 248, 'H', 'deeds' },
};
local CURRENCY_2 = {
    { 4, 'i', 'bayld' }, { 8, 'H', 'kinetic_unit' }, { 10, 'B', 'imprimaturs' }, { 11, 'B', 'mystical_canteen' },
    { 12, 'i', 'obsidian_fragment' }, { 16, 'H', 'lebondopt_wing' }, { 18, 'H', 'pulchridopt_wing' },
    { 20, 'i', 'mweya_plasm' }, { 24, 'B', 'ghastly_stone' }, { 25, 'B', 'ghastly_stone_1' },
    { 26, 'B', 'ghastly_stone_2' }, { 27, 'B', 'verdigris_stone' }, { 28, 'B', 'verdigris_stone_1' },
    { 29, 'B', 'verdigris_stone_2' }, { 30, 'B', 'wailing_stone' }, { 31, 'B', 'wailing_stone_1' },
    { 32, 'B', 'wailing_stone_2' }, { 33, 'B', 'snowslit_stone' }, { 34, 'B', 'snowslit_stone_1' },
    { 35, 'B', 'snowslit_stone_2' }, { 36, 'B', 'snowtip_stone' }, { 37, 'B', 'snowtip_stone_1' },
    { 38, 'B', 'snowtip_stone_2' }, { 39, 'B', 'snowdim_stone' }, { 40, 'B', 'snowdim_stone_1' },
    { 41, 'B', 'snowdim_stone_2' }, { 42, 'B', 'snoworb_stone' }, { 43, 'B', 'snoworb_stone_1' },
    { 44, 'B', 'snoworb_stone_2' }, { 45, 'B', 'leafslit_stone' }, { 46, 'B', 'leafslit_stone_1' },
    { 47, 'B', 'leafslit_stone_2' }, { 48, 'B', 'leaftip_stone' }, { 49, 'B', 'leaftip_stone_1' },
    { 50, 'B', 'leaftip_stone_2' }, { 51, 'B', 'leafdim_stone' }, { 52, 'B', 'leafdim_stone_1' },
    { 53, 'B', 'leafdim_stone_2' }, { 54, 'B', 'leaforb_stone' }, { 55, 'B', 'leaforb_stone_1' },
    { 56, 'B', 'leaforb_stone_2' }, { 57, 'B', 'duskslit_stone' }, { 58, 'B', 'duskslit_stone_1' },
    { 59, 'B', 'duskslit_stone_2' }, { 60, 'B', 'dusktip_stone' }, { 61, 'B', 'dusktip_stone_1' },
    { 62, 'B', 'dusktip_stone_2' }, { 63, 'B', 'duskdim_stone' }, { 64, 'B', 'duskdim_stone_1' },
    { 65, 'B', 'duskdim_stone_2' }, { 66, 'B', 'duskorb_stone' }, { 67, 'B', 'duskorb_stone_1' },
    { 68, 'B', 'duskorb_stone_2' }, { 69, 'B', 'pellucid_stone' }, { 70, 'B', 'fern_stone' },
    { 71, 'B', 'taupe_stone' }, { 74, 'H', 'escha_beads' }, { 76, 'i', 'escha_silt' }, { 80, 'i', 'potpourri' },
    { 84, 'i', 'current_hallmarks' }, { 88, 'i', 'total_hallmarks' }, { 92, 'i', 'gallantry' },
    { 96, 'i', 'crafter_points' }, { 100, 'B', 'fire_crystal_set' }, { 101, 'B', 'ice_crystal_set' },
    { 102, 'B', 'wind_crystal_set' }, { 103, 'B', 'earth_crystal_set' }, { 104, 'B', 'lightning_crystal_set' },
    { 105, 'B', 'water_crystal_set' }, { 106, 'B', 'light_crystal_set' }, { 107, 'B', 'dark_crystal_set' },
    { 108, 'B', 'mc_s_sr01_set' }, { 109, 'B', 'mc_s_sr02_set' }, { 110, 'B', 'mc_s_sr03_set' },
    { 111, 'B', 'liquefaction_spheres_set' }, { 112, 'B', 'induration_spheres_set' },
    { 113, 'B', 'detonation_spheres_set' }, { 114, 'B', 'scission_spheres_set' },
    { 115, 'B', 'impaction_spheres_set' }, { 116, 'B', 'reverberation_spheres_set' },
    { 117, 'B', 'transfixion_spheres_set' }, { 118, 'B', 'compression_spheres_set' },
    { 119, 'B', 'fusion_spheres_set' }, { 120, 'B', 'distortion_spheres_set' },
    { 121, 'B', 'fragmentation_spheres_set' }, { 122, 'B', 'gravitation_spheres_set' },
    { 123, 'B', 'light_spheres_set' }, { 124, 'B', 'darkness_spheres_set' }, { 128, 'i', 'silver_aman_voucher' },
    { 132, 'i', 'domain_points' }, { 136, 'i', 'domain_points_daily' }, { 140, 'i', 'mog_segments' },
    { 144, 'i', 'gallimaufry' }, { 148, 'H', 'is_accolades' }, { 152, 'i', 'temenos_units' },
    { 156, 'i', 'apollyon_units' },
};

-- The five 9-bit counts packed into the 64 bits at 216 of 0x113 (Bloodshed, Umbrage, Ritualistic,
-- Tutelary and Primacy plans), low bits first.
local PLANS = { { 0, 'bloodshed_plans' }, { 9, 'umbrage_plans' }, { 18, 'ritualistic_plans' }, { 27, 'tutelary_plans' }, { 36, 'primacy_plans' } };

local function read_currency_table(data, rows)
    for _, r in ipairs(rows) do
        local off, fmt, col = r[1], r[2], r[3];
        local size = (fmt == 'B') and 1 or ((fmt == 'H') and 2 or 4);
        if #data >= off + size then
            local v = struct.unpack('<' .. fmt, data, off + 1);
            if v ~= nil then seen.currencies[col] = v; end
        end
    end
end

local function read_currencies_1(data)
    read_currency_table(data, CURRENCY_1);
    if #data >= 224 then
        local lo = struct.unpack('<I', data, 217);
        local hi = struct.unpack('<I', data, 221);
        if lo and hi then
            for _, p in ipairs(PLANS) do
                local shift, col = p[1], p[2];
                local v;
                if shift + 9 <= 32 then v = bit.band(bit.rshift(lo, shift), 0x1FF);
                elseif shift >= 32 then v = bit.band(bit.rshift(hi, shift - 32), 0x1FF);
                else v = bit.band(bit.bor(bit.rshift(lo, shift), bit.lshift(hi, 32 - shift)), 0x1FF); end
                seen.currencies[col] = v;
            end
        end
    end
    seen.currencies1_at = os.time();
end

local function read_currencies_2(data)
    read_currency_table(data, CURRENCY_2);
    seen.currencies2_at = os.time();
end

local JOB_COUNT = 22;        -- 1 WAR .. 22 RUN
local COMBAT_SKILLS = 48;    -- Ashita combat skill indexes
local CRAFT_SKILLS = 10;     -- fishing, woodworking, smithing, goldsmithing, clothcraft, leathercraft, bonecraft, alchemy, cooking, synergy
local SPELL_MAX = 1024;         -- exact client table: 0x0AA MagicDataTbl, 128 bytes
local ABILITY_MAX = 1024;       -- exact client tables: 0x0AC JobAbilities 64 bytes + PetAbilities 64 bytes
local WEAPONSKILL_MAX = 512;     -- exact client table: 0x0AC WeaponSkills, 64 bytes (it was 256, half of it)
local TRAIT_MAX = 256;          -- exact client table: 0x0AC Traits, 32 bytes
local KEYITEM_MAX = 4096;       -- exact client tables: 0x055, 8 tables of 512 (it was 3072, so every key item from 3072 up was lost)
local CONTAINERS = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
local EQUIP_SLOTS = 16;

-- Ashita only has a storage's contents (and GetContainerCountMax for it) once the client has been
-- sent them, which for Inventory/Wardrobe/Safe/etc happens on zone in, but for Mog Safe 2 and
-- Wardrobes 2-8 only happens once that tab has actually been opened this session - equipping from
-- one works with no such requirement, so a capture can read id 0 for an item worn straight out of
-- an unopened wardrobe, with the same wardrobe's own slot list coming back empty alongside it.
local STORAGE_NAME = {
    [0] = 'Inventory', [1] = 'Mog Safe', [2] = 'Storage', [3] = 'Temporary Items', [4] = 'Mog Locker',
    [5] = 'Mog Satchel', [6] = 'Mog Sack', [7] = 'Mog Case', [8] = 'Mog Wardrobe', [9] = 'Mog Safe 2',
    [10] = 'Mog Wardrobe 2', [11] = 'Mog Wardrobe 3', [12] = 'Mog Wardrobe 4', [13] = 'Mog Wardrobe 5',
    [14] = 'Mog Wardrobe 6', [15] = 'Mog Wardrobe 7', [16] = 'Mog Wardrobe 8',
};

-- The automaton comes in the 0x044 extended-job packet for PUP, sent at login and on every change to or from
-- Puppetmaster (main or sub). Layout from the server's own copy of it (0x044_extended_job_pup): job at 0x04,
-- head 0x08, frame 0x09, 12 attachments from 0x0A, unlocked heads and frames (u32 each) at 0x18 and 0x1C,
-- 8 u32 of unlocked attachments at 0x38, the name (16 bytes) at 0x58. Nothing else tells the client's memory
-- managers about it, so a character that never was Puppetmaster this session has no automaton in the snapshot.
local PUP_JOB = 18;

local function read_automaton(data)
    if #data < 0x68 then return; end
    if struct.unpack('<B', data, 0x05) ~= PUP_JOB then return; end
    local a = T{ head = struct.unpack('<B', data, 0x09), frame = struct.unpack('<B', data, 0x0A), attachments = T{}, unlocked_attachments = T{} };
    for i = 0, 11 do a.attachments:append((struct.unpack('<B', data, 0x0B + i))); end
    a.unlocked_heads = struct.unpack('<I', data, 0x19);
    a.unlocked_frames = struct.unpack('<I', data, 0x1D);
    for i = 0, 7 do a.unlocked_attachments:append((struct.unpack('<I', data, 0x39 + i * 4))); end
    a.name = (data:sub(0x59, 0x68):gsub('%z.*', ''));
    seen.automaton = a;
    seen.automaton_at = os.time();
end

-- The game only shows the skills of the job you are on (packet 0x062, sent again on every job change), so
-- each job pair seen this session keeps its own reading. Change jobs in your Mog House before /capture and
-- every job's skills come along; anything never seen is filled in at its cap on import.
local function read_job_skills()
    local player = AshitaCore:GetMemoryManager():GetPlayer();
    if player:GetLoginStatus() ~= 2 then return; end
    local main, sub = player:GetMainJob(), player:GetSubJob();
    if main == nil or main == 0 then return; end
    local r = T{ main = main, main_level = player:GetMainJobLevel(), sub = sub, sub_level = player:GetSubJobLevel(), combat = T{} };
    for i = 0, COMBAT_SKILLS - 1 do
        local s = player:GetCombatSkill(i);
        if s then r.combat[tostring(i)] = T{ skill = s:GetSkill(), capped = s:IsCapped() }; end
    end
    seen.skills_by_job[('%d/%d'):format(main, sub or 0)] = r;
end

local function say(msg)
    print(('\30\08[charcapture]\30\01 %s'):format(msg));
end

local function server_name()
    local cmd = '';
    pcall(function() cmd = AshitaCore:GetConfigurationManager():GetString('boot', 'ashita.boot', 'command') or ''; end);
    local file = '';
    pcall(function() file = AshitaCore:GetConfigurationManager():GetString('boot', 'ashita.boot', 'file') or ''; end);
    local server = cmd:match('%-%-server%s+(%S+)');
    if server then return server; end
    if file:lower():find('xiloader', 1, true) then return 'private'; end
    return 'retail';
end

local function safe(f, default)
    local ok, v = pcall(f);
    if ok and v ~= nil then return v; end
    return default;
end

-- Reads one bag slot by slot. A bag the game has locked (a Mog Wardrobe that has lapsed shows its
-- gear but will not let it be touched) can report a size of 0 while its slots are still filled, so
-- every slot a bag can hold is read whatever size the game reports; an empty slot reads as id 0 and
-- is skipped. Returns the items found and the reported size.
local CONTAINER_SLOTS = 80;

local function read_container(inv, c)
    local max = safe(function() return inv:GetContainerCountMax(c); end, 0) or 0;
    local list = T{};
    for slot = 1, math.max(max, CONTAINER_SLOTS) do
        local it = safe(function() return inv:GetContainerItem(c, slot); end, nil);
        if it and it.Id ~= 0 and it.Id ~= 65535 and it.Count > 0 then
            list:append(T{ slot = slot, id = it.Id, count = it.Count, flags = it.Flags, price = it.Price, extra = hex(it.Extra) });
        end
    end
    return list, max;
end

local function capture()
    local mm = AshitaCore:GetMemoryManager();
    local player = mm:GetPlayer();
    local inv = mm:GetInventory();
    local party = mm:GetParty();
    local ent = mm:GetEntity();

    if player:GetLoginStatus() ~= 2 then say('log in first'); return nil; end
    local idx = party:GetMemberTargetIndex(0);
    local name = party:GetMemberName(0);
    if name == nil or #name == 0 then name = safe(function() return ent:GetName(idx); end, 'Unknown'); end

    local snap = T{
        schema = 1, addon = 'charcapture', addon_version = addon.version,
        captured_at = os.date('!%Y-%m-%dT%H:%M:%SZ'),
        captured_on = server_name(),
        character = T{
            name = name,
            server_id = party:GetMemberServerId(0),
            race = safe(function() return ent:GetRace(idx); end, 0),
            face = safe(function() return ent:GetLookHair(idx); end, 0),
            size = safe(function() return ent:GetModelSize(idx); end, 0),
            nation = safe(function() return player:GetNation(); end, 0),
            rank = safe(function() return player:GetRank(); end, 0),
            rank_points = safe(function() return player:GetRankPoints(); end, 0),
            title = safe(function() return player:GetTitle(); end, 0),
            homepoint = safe(function() return player:GetHomepoint(); end, 0),
            residence = safe(function() return player:GetResidence(); end, 0),
        },
        jobs = T{
            main = player:GetMainJob(), main_level = player:GetMainJobLevel(),
            sub = player:GetSubJob(), sub_level = player:GetSubJobLevel(),
            levels = T{}, master_levels = T{}, job_points = T{},
            merits = T{ points = safe(function() return player:GetMeritPoints(); end, 0), max = safe(function() return player:GetMeritPointsMax(); end, 0) },
            limit_points = safe(function() return player:GetLimitPoints(); end, 0),
            exp = T{ current = safe(function() return player:GetExpCurrent(); end, 0), needed = safe(function() return player:GetExpNeeded(); end, 0) },
        },
        skills = T{ combat = T{}, craft = T{}, by_job = T{} },
        spells = T{}, abilities = T{}, weaponskills = T{}, traits = T{}, key_items = T{},
        gil = 0,
        inventory = T{},
        equipment = T{},
        look = T{
            hair = safe(function() return ent:GetLookHair(idx); end, 0),
            head = safe(function() return ent:GetLookHead(idx); end, 0),
            body = safe(function() return ent:GetLookBody(idx); end, 0),
            hands = safe(function() return ent:GetLookHands(idx); end, 0),
            legs = safe(function() return ent:GetLookLegs(idx); end, 0),
            feet = safe(function() return ent:GetLookFeet(idx); end, 0),
            main = safe(function() return ent:GetLookMain(idx); end, 0),
            sub = safe(function() return ent:GetLookSub(idx); end, 0),
            ranged = safe(function() return ent:GetLookRanged(idx); end, 0),
        },
        -- from the menus, when they were opened this session (see the packet notes at the top)
        merit_upgrades = seen.merits_at and seen.merits or nil,
        job_point_upgrades = seen.job_points_at and seen.job_points or nil,
        quest_mission_packets = seen.logs_at and seen.logs or nil,
        currencies = (seen.currencies1_at or seen.currencies2_at) and seen.currencies or nil,
        personal_chocobo = seen.chocobo_at and T{ properties = seen.chocobo } or nil,
        automaton = seen.automaton_at and seen.automaton or nil,
        -- the exemplar bar: the game keeps one job as the Mastery job, and only its points are in memory
        mastery = T{ job = safe(function() return player:GetMasteryJob(); end, 0), level = safe(function() return player:GetMasteryJobLevel(); end, 0),
                     exp = safe(function() return player:GetMasteryExp(); end, 0), needed = safe(function() return player:GetMasteryExpNeeded(); end, 0) },
        not_captured = T{ 'fame (the game never sends the number)', 'linkshells', 'mog house layout' },
    };
    if not seen.merits_at then snap.not_captured:append('merit upgrades (open the Merit Points menu, then /capture again)'); end
    if not seen.job_points_at then snap.not_captured:append('job point upgrades (open the Job Points menu, then /capture again)'); end
    if not seen.logs_at then snap.not_captured:append('quests and missions (zone once with charcapture loaded, then /capture again)'); end
    if not seen.currencies1_at then snap.not_captured:append('currencies, first tab (open Profile > Currencies, then /capture again)'); end
    if not seen.currencies2_at then snap.not_captured:append('currencies, second tab (open the Currencies 2 tab of the same menu, then /capture again)'); end
    if not seen.automaton_at and safe(function() return player:GetJobLevel(PUP_JOB); end, 0) > 1 then snap.not_captured:append('your automaton (switch to Puppetmaster once, main or sub, then /capture again)'); end
    if not seen.chocobo_at then snap.not_captured:append('your own chocobo (call it with the Chocobo Whistle and ride it, then /capture again; skip this if you have none)'); end

    -- jobs
    for job = 1, JOB_COUNT do
        snap.jobs.levels[tostring(job)] = safe(function() return player:GetJobLevel(job); end, 0);
        snap.jobs.master_levels[tostring(job)] = safe(function() return player:GetJobMasterLevel(job); end, 0);
        snap.jobs.job_points[tostring(job)] = T{
            points = safe(function() return player:GetJobPoints(job); end, 0),
            spent = safe(function() return player:GetJobPointsSpent(job); end, 0),
            capacity = safe(function() return player:GetCapacityPoints(job); end, 0),
        };
    end

    -- skills: this job's reading at the top, every job pair seen this session under by_job
    pcall(read_job_skills);
    for k, v in pairs(seen.skills_by_job) do snap.skills.by_job[k] = v; end
    for i = 0, COMBAT_SKILLS - 1 do
        local s = safe(function() return player:GetCombatSkill(i); end, nil);
        if s then snap.skills.combat[tostring(i)] = T{ skill = safe(function() return s:GetSkill(); end, 0), rank = safe(function() return s:GetRank(); end, 0), capped = safe(function() return s:IsCapped(); end, false) }; end
    end
    for i = 0, CRAFT_SKILLS - 1 do
        local s = safe(function() return player:GetCraftSkill(i); end, nil);
        if s then snap.skills.craft[tostring(i)] = T{ skill = safe(function() return s:GetSkill(); end, 0), rank = safe(function() return s:GetRank(); end, 0), capped = safe(function() return s:IsCapped(); end, false) }; end
    end

    -- known things: every id the client says yes to
    for id = 0, SPELL_MAX - 1 do if safe(function() return player:HasSpell(id); end, false) then snap.spells:append(id); end end
    for id = 0, ABILITY_MAX - 1 do if safe(function() return player:HasAbility(id); end, false) then snap.abilities:append(id); end end
    for id = 0, WEAPONSKILL_MAX - 1 do if safe(function() return player:HasWeaponSkill(id); end, false) then snap.weaponskills:append(id); end end
    for id = 0, TRAIT_MAX - 1 do if safe(function() return player:HasTrait(id); end, false) then snap.traits:append(id); end end
    for id = 0, KEYITEM_MAX - 1 do if safe(function() return player:HasKeyItem(id); end, false) then snap.key_items:append(id); end end

    -- bags
    local item_count = 0;
    local container_unsynced = T{};    -- bags the game gave nothing for (not opened this session, or locked)
    local container_locked = T{};      -- bags the game reports as size 0 but whose items were read anyway
    for _, c in ipairs(CONTAINERS) do
        local list, max = read_container(inv, c);
        if c == 0 then
            -- slot 0 of the inventory is gil
            local g = safe(function() return inv:GetContainerItem(0, 0); end, nil);
            if g and g.Id == 65535 then snap.gil = g.Count; end
        elseif max == 0 then
            if #list == 0 then container_unsynced[c] = true; else container_locked:append(c); end
        end
        item_count = item_count + #list;
        snap.inventory[tostring(c)] = list;
    end
    if #container_locked > 0 then
        local names = T{};
        for _, c in ipairs(container_locked) do names:append(STORAGE_NAME[c] or ('storage ' .. c)); end
        snap.not_captured:append(('%s reads as locked by the game (size 0) but its items were captured: check they are all there'):format(table.concat(names, ', ')));
    end

    -- equipment
    local unsynced_worn_seen = {};   -- container id -> true, plain table so :append below can't collide with it
    local unsynced_worn = T{};       -- the same containers, in order, for the message
    for slot = 0, EQUIP_SLOTS - 1 do
        local e = safe(function() return inv:GetEquippedItem(slot); end, nil);
        if e and e.Index ~= 0 then
            local container = math.floor(bit.band(e.Index, 0xFF00) / 0x0100);
            local index = e.Index % 0x0100;
            local it = safe(function() return inv:GetContainerItem(container, index); end, nil);
            snap.equipment[tostring(slot)] = T{ container = container, slot = index, id = it and it.Id or 0 };
            if container_unsynced[container] and not unsynced_worn_seen[container] then
                unsynced_worn_seen[container] = true;
                unsynced_worn:append(container);
            end
        end
    end
    if #unsynced_worn > 0 then
        local names = T{};
        for _, c in ipairs(unsynced_worn) do names:append(STORAGE_NAME[c] or ('storage ' .. c)); end
        snap.not_captured:append(('gear worn from %s (open it from the main menu once, even if the wardrobe is unpaid: it still shows its gear; then /capture again)'):format(table.concat(names, ', ')));
    end

    -- write
    local dir = ('%sconfig\\charcapture\\'):format(AshitaCore:GetInstallPath());
    ashita.fs.create_directory(dir);
    local path = dir .. name:gsub('[^%w]', '') .. '.json';
    local f = io.open(path, 'w');
    if not f then say('could not write ' .. path); return nil; end
    f:write(json.encode(snap));
    f:close();

    local res = AshitaCore:GetResourceManager();
    local job = res:GetString('jobs.names_abbr', snap.jobs.main) or tostring(snap.jobs.main);
    last_summary = ('%s, %s%d, %d items across all bags, %d gil, %d spells, %d key items -> %s'):format(name, job, snap.jobs.main_level, item_count, snap.gil, #snap.spells, #snap.key_items, path);
    say(last_summary);
    if #unsynced_worn > 0 then
        local names = T{};
        for _, c in ipairs(unsynced_worn) do names:append(STORAGE_NAME[c] or ('storage ' .. c)); end
        say(('Open %s once from the main menu (Ashita has not loaded it this session, so the gear you have worn from it came out blank; a wardrobe that is not paid for this month still shows its gear there), then /capture again.'):format(table.concat(names, ', ')));
    end
    say('now send it from the launcher: Capture > Send to Vanadreams.');
    return path;
end

ashita.events.register('command', 'charcapture_cmd', function (e)
    local args = e.command:args();
    if #args == 0 or (args[1] ~= '/capture' and args[1] ~= '/charcapture') then return; end
    e.blocked = true;
    local sub = (args[2] or ''):lower();
    if sub == 'show' then
        say(last_summary or 'nothing captured yet; /capture to take a snapshot');
    else
        capture();
    end
end);

ashita.events.register('packet_in', 'charcapture_packet_in', function (e)
    if e.id == 0x08C then pcall(read_merits, e.data);
    elseif e.id == 0x08D then pcall(read_job_points, e.data);
    elseif e.id == 0x113 then pcall(read_currencies_1, e.data);
    elseif e.id == 0x118 then pcall(read_currencies_2, e.data);
    elseif e.id == 0x056 then pcall(read_log, e.data);
    elseif e.id == 0x067 then pcall(read_chocobo_sync, e.data);
    elseif e.id == 0x00D then pcall(read_chocobo_update, e.data);
    elseif e.id == 0x044 then pcall(read_automaton, e.data);
    elseif e.id == 0x062 then seen.skills_due = os.clock() + 2; end   -- read once the client has applied it
end);

ashita.events.register('d3d_present', 'charcapture_present', function ()
    if seen.skills_due and os.clock() >= seen.skills_due then
        seen.skills_due = nil;
        pcall(read_job_skills);
    end
end);

ashita.events.register('load', 'charcapture_load', function ()
    say('loaded. Zone once, open the Merit Points, Job Points and both Currencies menus, then /capture writes your character snapshot. The game only shows the skills of the job you are on: switch to your other jobs in your Mog House first and their skills come too.');
end);
