--[[
    vanagear: which sets to build for the job the player is on, and what each one values.
]]--

local roles = require('vg.roles');
local vdata = require('vg.data');

local plan = {};

local function copy(t)
    if (type(t) ~= 'table') then return t; end
    local c = {};
    for k, v in pairs(t) do c[k] = copy(v); end
    return c;
end

-- roles.lua's mode with the job file's tweaks laid over it, stat by stat
local function mode(name, tweaks)
    local m = copy(roles.Modes[name]) or {};
    m.Name = name;
    local t = tweaks and tweaks[name];
    if (t ~= nil) then
        for _, part in ipairs({ 'Stats', 'Caps', 'Pet', 'PetCaps' }) do
            if (t[part] ~= nil) then
                m[part] = m[part] or {};
                for k, v in pairs(t[part]) do m[part][k] = v; end
            end
        end
    end
    return m;
end

plan.Mode = mode;

local jobCache = {};
plan.Job = function(abbr)
    if (jobCache[abbr] == nil) then
        local path = string.format('%saddons\\vanagear\\jobs\\%s.lua', AshitaCore:GetInstallPath(), abbr);
        local chunk = loadfile(path);
        jobCache[abbr] = chunk and chunk() or { Fighter = true };
    end
    return jobCache[abbr];
end
plan.Forget = function() jobCache = {}; end

-- the server weapon skill of the item worn in a slot, or nil
local function wornSkill(equipment, slotName)
    local e = equipment[slotName];
    if (e == nil) or (e.Item == nil) then return nil; end
    local s = vdata.Items()[e.Item.Id];
    return s and s.skill or nil;
end

-- One weaponskill's mode, from the stats the server's script reads.
local function wsMode(ws, displayName)
    local m = { Name = displayName, Stats = {} };
    local s = m.Stats;
    local scale = 2;
    for stat, v in pairs(ws.wsc) do
        s[stat] = v * scale;
    end
    if (ws.kind == 'magical') then
        s.MATT = 2; s.MACC = 0.6; s.MAGIC_DAMAGE = 1.2;
        s.ALL_WSDMG_FIRST_HIT = 3; s.TP_BONUS = 0.008;
    elseif (ws.kind == 'ranged') then
        s.RATT = 0.6; s.RACC = 0.5; s.ALL_WSDMG_FIRST_HIT = 2.5; s.TP_BONUS = 0.008;
        s.AGI = (s.AGI or 0) + 0.2;
    else
        s.ATT = 0.5; s.ACC = 0.35; s.TP_BONUS = 0.008; s.CRIT_DMG_INCREASE = 0.3;
        if (ws.hits > 1) then
            s.ALL_WSDMG_ALL_HITS = 3; s.ALL_WSDMG_FIRST_HIT = 1.5; s.ANY_FTP_BONUS = 0.1;
            s.DOUBLE_ATTACK = 0.8; s.TRIPLE_ATTACK = 1.2;
        else
            s.ALL_WSDMG_ALL_HITS = 3; s.ALL_WSDMG_FIRST_HIT = 2.5; s.ANY_FTP_BONUS = 0.04;
        end
    end
    return m;
end

plan.WsMode = wsMode;

-- The set name a weaponskill's set goes by: its name with spaces and punctuation taken out,
-- as the starter profiles already name them (Savage Blade -> SavageBlade).
plan.WsSetName = function(name)
    return (string.gsub(name, '[^%w]', ''));
end

--[[
    Returns a list of { Name = set name, Mode = mode table, Slots = { slot indexes } } and the
    reasons slots were held, for the job, subjob and weapons the player has right now.
    opts.Weapons: let the builder choose Main, Sub and Range.
]]--
-- A job file's Bands are level ranges, each with its own Tweaks and Modes: { To = 30, Tweaks = {...} }
-- in rising order. The band the player's level falls in is laid over the job's all-level Tweaks,
-- so a 70s character is not scored by what a 99 wants (the guides read for M401).
plan.Band = function(job, level)
    for _, b in ipairs(job.Bands or {}) do
        if (level <= (b.To or 99)) then return b; end
    end
    return nil;
end

local function bandTweaks(job, level)
    local band = plan.Band(job, level);
    if (band == nil) or (band.Tweaks == nil) then return job.Tweaks or {}; end
    local out = copy(job.Tweaks or {});
    for modeName, t in pairs(band.Tweaks) do
        out[modeName] = out[modeName] or {};
        for _, part in ipairs({ 'Stats', 'Caps', 'Pet', 'PetCaps' }) do
            if (t[part] ~= nil) then
                out[modeName][part] = out[modeName][part] or {};
                for k, v in pairs(t[part]) do out[modeName][part][k] = v; end
            end
        end
    end
    return out;
end
plan.BandTweaks = bandTweaks;

--[[
    What the player already has toward a stat without gear, by stat name: the job traits of the main
    job at its level and of the support job at its level (each trait at its highest rank by then,
    data\traits.lua), and the main job's job point gifts up to the points spent (data\gifts.lua).
    Merits are not counted: the client does not show how a character spent them.
]]--
plan.Bonus = function(who)
    local out = {};
    local function addTraits(jobId, level)
        local best = {};   -- trait..mod -> the highest rank held at this level
        for _, t in ipairs((vdata.Traits() or {})[jobId] or {}) do
            if (t.level <= level) then
                local key = t.trait .. ':' .. t.mod;
                if (best[key] == nil) or (t.rank > best[key].rank) then best[key] = t; end
            end
        end
        for _, t in pairs(best) do
            local name = vdata.ModName(t.mod);
            if (name ~= nil) then out[name] = (out[name] or 0) + t.v; end
        end
    end
    if (who.Job ~= nil) and (who.Job > 0) then addTraits(who.Job, who.Level or 99); end
    if (who.Sub ~= nil) and (who.Sub > 0) then addTraits(who.Sub, who.SubLevel or 0); end
    for _, g in ipairs((vdata.Gifts() or {})[who.Job or 0] or {}) do
        if (g.jp <= (who.JobPoints or 0)) then
            local name = vdata.ModName(g.mod);
            if (name ~= nil) then out[name] = (out[name] or 0) + g.v; end
        end
    end
    return out;
end

-- A cap gear works toward, less what the job already has toward it (a Red Mage's Fast Cast trait
-- leaves gear less to reach the 80% fast cast cap). Caps are in the units players read, bonuses in
-- the server's.
local function lowerCaps(m, bonus)
    if (m.Caps == nil) then return; end
    local score = require('vg.score');
    local caps = {};
    for stat, cap in pairs(m.Caps) do
        local have = bonus[stat] or 0;
        for _, part in ipairs(score.Virtual[stat] or {}) do have = have + (bonus[part] or 0); end
        have = have / (score.Unit[stat] or 1);
        if (cap < 0) then caps[stat] = math.min(0, cap + have); else caps[stat] = math.max(0, cap - have); end
    end
    m.Caps = caps;
end
plan.LowerCaps = lowerCaps;

-- Job effects with a set of their own, by the job and level that give them (abilities from the job
-- guides; mainOnly where a support job never reaches it).
plan.EffectSets = {
    { set = 'Saboteur', job = 'RDM', level = 83, mainOnly = true },
    { set = 'SneakAttack', job = 'THF', level = 15 },
    { set = 'TrickAttack', job = 'THF', level = 30 },
    { set = 'SneakTrickAttack', job = 'THF', level = 30 },
    { set = 'Cure_Solace', job = 'WHM', level = 40 },
    { set = 'Souleater', job = 'DRK', level = 30 },
    { set = 'LastResort', job = 'DRK', level = 15 },
    { set = 'Enmity', job = 'PLD', level = 1, mainOnly = true },
    { set = 'SpellInterrupt', job = 'PLD', level = 1, mainOnly = true },
    { set = 'Tank_Main', job = 'PLD', level = 1, mainOnly = true },
    { set = 'Tank_MEVA', job = 'PLD', level = 1, mainOnly = true },
    { set = 'Covering', job = 'PLD', level = 35, mainOnly = true },
    { set = 'Enmity', job = 'RUN', level = 1, mainOnly = true },
    { set = 'SpellInterrupt', job = 'RUN', level = 1, mainOnly = true },
    { set = 'Tank_Main', job = 'RUN', level = 1, mainOnly = true },
    { set = 'Tank_MEVA', job = 'RUN', level = 1, mainOnly = true },
    { set = 'FootworkTP', job = 'MNK', level = 65, mainOnly = true },
    { set = 'Futae', job = 'NIN', level = 77, mainOnly = true },
    { set = 'Breath', job = 'DRG', level = 1, mainOnly = true },
    { set = 'ChainAffinity', job = 'BLU', level = 40 },
    { set = 'TripleShot', job = 'COR', level = 87, mainOnly = true },
    { set = 'PetRangedWS', job = 'PUP', level = 1, mainOnly = true },
    { set = 'Sublimation', job = 'SCH', level = 35 },
    { set = 'Stratagem', job = 'SCH', level = 55, mainOnly = true },
    { set = 'BurstAffinity', job = 'BLU', level = 25 },
    { set = 'PetIdle', job = 'SMN', level = 1, mainOnly = true },
    { set = 'Summoning', job = 'SMN', level = 1, mainOnly = true },
    { set = 'ThirdEye', job = 'SAM', level = 15 },
    { set = 'Hasso', job = 'SAM', level = 25 },
    { set = 'Seigan', job = 'SAM', level = 35 },
    { set = 'Sengikori', job = 'SAM', level = 77, mainOnly = true },
    { set = 'Barrage', job = 'RNG', level = 30 },
    { set = 'Velocity', job = 'RNG', level = 45 },
    { set = 'Velocity_Preshot', job = 'RNG', level = 45 },
    { set = 'TrueShot', job = 'RNG', level = 78, mainOnly = true },
    { set = 'DoubleShot', job = 'RNG', level = 79, mainOnly = true },
};

plan.For = function(who, equipment, opts)
    local job = plan.Job(who.JobName);
    local tweaks = bandTweaks(job, who.Level or 99);
    local band = plan.Band(job, who.Level or 99);
    local names, seen = {}, {};
    local function add(list)
        for _, n in ipairs(list) do
            if (not seen[n]) then seen[n] = true; names[#names + 1] = n; end
        end
    end

    add(roles.Always);
    if (job.Support) then add(roles.Support);
    elseif (job.Fighter ~= false) then add(roles.Fighter); end

    local skills, haveSkill = {}, {};
    for _, s in ipairs(job.Skills or {}) do
        if (not haveSkill[s]) then haveSkill[s] = true; skills[#skills + 1] = s; end
    end
    for _, s in ipairs(roles.SubSkills[who.SubName] or {}) do
        if (not haveSkill[s]) then haveSkill[s] = true; skills[#skills + 1] = s; end
    end
    if (#skills > 0) then
        add(roles.Caster);
        for _, s in ipairs(skills) do add(roles.SkillSets[s] or {}); end
    end

    local rangeSkill = wornSkill(equipment, 'Range');
    local shooting = (rangeSkill == 25) or (rangeSkill == 26) or (rangeSkill == 27);
    if (job.Shooter) or (shooting) then add(roles.Shooter); end
    if (job.Pet) then add(roles.Pet); end
    add(job.Modes or {});
    if (band ~= nil) then add(band.Modes or {}); end

    -- Player weapons and ammo are never chosen by scoring, even with an old Weapons=true
    -- preference. Only the player or an explicit profile/action set chooses these slots.
    local slots, held = {}, {};
    for i = 1, 16 do
        local keep = false;
        if (i <= 4) then
            keep = true; -- not a lock: explicit sets and the slot's drop-down still work
        elseif (opts.Locked ~= nil) and (opts.Locked[i]) then
            keep = true; held[i] = 'locked with /vg lock';
        end
        if (not keep) then slots[#slots + 1] = i; end
    end

    -- dual wield is worth something only with a weapon in the off hand
    local subSkill = wornSkill(equipment, 'Sub');
    local dualWield = (subSkill ~= nil) and (subSkill >= 1) and (subSkill <= 12);

    local out = {};
    for _, n in ipairs(names) do
        local m = mode(n, tweaks);
        if (string.sub(n, 1, 3) == 'Tp_') then
            if (dualWield) then
                m.Stats.DUAL_WIELD = m.Stats.DUAL_WIELD or 1.2;
                m.Caps = m.Caps or {};
                m.Caps.DUAL_WIELD = m.Caps.DUAL_WIELD or 35;
            else
                -- a template may weight dual wield for its band; under a shield or a two-hander it is worth nothing
                m.Stats.DUAL_WIELD = nil;
            end
        end
        out[#out + 1] = { Name = n, Mode = m, Slots = slots };
    end

    -- Job ability sets, worn as the ability goes off, under the ability's own name with spaces
    -- taken out (Blood Rage -> BloodRage). What each one values is what the server reads when
    -- it goes off (data\abilities.lua, from the ability scripts); the job file's Abilities add
    -- to that or change a weight, and its AbilityBase is laid under every ability set.
    local resources = AshitaCore:GetResourceManager();
    local byName = {};
    local subLevel = who.SubLevel or 0;
    for id, a in pairs(vdata.Abilities()) do
        local mine = ((a.job == who.Job) and (a.level <= who.Level)) or ((a.job == who.Sub) and (a.level <= subLevel));
        if (mine) then
            local res = resources:GetAbilityById(id + 0x200);
            local display = (res ~= nil) and res.Name[1] or a.key;
            local m = { Name = plan.WsSetName(display), Stats = {} };
            for k, v in pairs(job.AbilityBase or {}) do m.Stats[k] = v; end
            for _, mod in ipairs(a.mods) do
                -- a mod no gear carries cannot be dressed for
                if (mod ~= 'ONE_HOUR_RECAST') and (vdata.ModId(mod) ~= nil) then m.Stats[mod] = 1; end
            end
            byName[display] = m;
        end
    end
    for ability, extra in pairs(job.Abilities or {}) do
        local m = byName[ability] or { Name = plan.WsSetName(ability), Stats = {} };
        for _, part in ipairs({ 'Stats', 'Caps', 'Pet', 'PetCaps' }) do
            if (extra[part] ~= nil) then
                m[part] = m[part] or {};
                for k, v in pairs(extra[part]) do m[part][k] = v; end
            end
        end
        byName[ability] = m;
    end
    for ability, m in pairs(byName) do
        if (next(m.Stats) ~= nil) or (m.Pet ~= nil) then
            out[#out + 1] = { Name = m.Name, Mode = m, Slots = slots, Ability = ability };
        end
    end

    -- a set for every weaponskill the weapons in hand can use
    local skillsInHand = {};
    local main = wornSkill(equipment, 'Main');
    if (main ~= nil) then skillsInHand[main] = true; end
    if (rangeSkill ~= nil) then skillsInHand[rangeSkill] = true; end
    local resources = AshitaCore:GetResourceManager();
    local player = AshitaCore:GetMemoryManager():GetPlayer();
    local function skillOf(skillId)
        if (player.GetCombatSkill == nil) then return 999; end
        local s = player:GetCombatSkill(skillId);
        return (s ~= nil) and s:GetSkill() or 999;
    end
    for id, ws in pairs(vdata.Weaponskills()) do
        -- only the weaponskills the weapons in hand can use at the skill the player has
        if (skillsInHand[ws.skill]) and (next(ws.wsc) ~= nil) and (ws.level <= skillOf(ws.skill)) then
            local res = resources:GetAbilityById(id);
            local display = (res ~= nil) and res.Name[1] or ws.key;
            local m = wsMode(ws, display);
            -- what the wiki weaponskill pages add (vg\wsguide.lua), then the job file's Weaponskills
            -- and the band's, each laid over the script-derived stats, stat by stat (M401, M402)
            local wiki = require('vg.wsguide')[display];
            if (wiki ~= nil) then
                for k, v in pairs(wiki) do m.Stats[k] = v; end
            end
            for _, from in ipairs({ job.Weaponskills, band and band.Weaponskills }) do
                local extra = from and from[display];
                if (extra ~= nil) then
                    for _, part in ipairs({ 'Stats', 'Caps' }) do
                        if (extra[part] ~= nil) then
                            m[part] = m[part] or {};
                            for k, v in pairs(extra[part]) do m[part][k] = v; end
                        end
                    end
                end
            end
            out[#out + 1] = { Name = plan.WsSetName(display), Mode = m, Slots = slots, Weaponskill = display };
        end
    end

    -- a set for every spell the player knows and can cast at this level, main job and support
    -- job (Ferrin 19 Oct 2026: "it also needs to detect spells on ninja, and the other spelled
    -- jobs"). Each values what its kind of spell needs; a spell whose set name is already a
    -- set (Cure, Stoneskin) keeps that one.
    local have = {};
    for _, o in ipairs(out) do have[string.lower(o.Name)] = true; end
    local okSpells, known = pcall(plan.Spells, who);
    for _, s in ipairs(okSpells and known or {}) do
        local setName = plan.WsSetName(s.Name);
        local kind = plan.SpellMode(s.Skill, s.Name);
        -- a spell with a mode of its own (Dread Spikes) is built from it, not from its kind's
        if (roles.Modes[setName] ~= nil) then kind = setName; end
        if (kind ~= nil) and (not have[string.lower(setName)]) then
            have[string.lower(setName)] = true;
            local m = mode(kind, tweaks);
            m.Name = setName;
            -- an enfeeble reads one stat against the target's (enfeebling_spell.lua): MND for Paralyze,
            -- Slow, Distract, INT for Blind, Poison, Sleep; the other is worth nothing to it. GetAwayCoxn's
            -- RDM.lua has Mind_Enfeebling and Int_Enfeebling for this
            local enf = vdata.Enfeeble(s.Name);
            if (enf ~= nil) and (m.Stats ~= nil) then
                local other = (enf.stat == 'MND') and 'INT' or 'MND';
                m.Stats[enf.stat] = math.max(m.Stats[enf.stat] or 0, m.Stats[other] or 0, 0.6) + 0.4;
                m.Stats[other] = nil;
            end
            out[#out + 1] = { Name = setName, Mode = m, Slots = slots, Spell = s.Name, SameAs = (kind ~= setName) and kind or nil };
        end
    end

    -- sets for the job effects the templates lay over a phase while they are up (M419), when the
    -- player has what gives them: the ability at their main or support job level, or a spell they know
    local function addEffect(name, onlySlots)
        if (not have[string.lower(name)]) then
            have[string.lower(name)] = true;
            out[#out + 1] = { Name = name, Mode = mode(name, tweaks), Slots = onlySlots or slots };
        end
    end
    -- the obi sets, waist only, for anyone with a spell to cast (the templates wear Obi_<element>,
    -- else Obi, over a spell whose element is the day's or the weather's)
    -- or over a weaponskill whose damage takes an element (Trueflight, Aeolian Edge)
    local elementalWs = false;
    for _, o in ipairs(out) do
        if (o.Weaponskill ~= nil) and (vdata.WsElement(o.Weaponskill) ~= nil) then elementalWs = true; end
    end
    if ((okSpells) and (known ~= nil) and (#known > 0)) or (elementalWs) then
        for _, n in ipairs({ 'Obi', 'Obi_Fire', 'Obi_Ice', 'Obi_Wind', 'Obi_Earth', 'Obi_Thunder', 'Obi_Water', 'Obi_Light', 'Obi_Dark' }) do
            addEffect(n, { 11 });
        end
    end
    for _, e in ipairs(plan.EffectSets) do
        local main = (who.JobName == e.job) and (who.Level >= e.level);
        local sub = (who.SubName == e.job) and ((who.SubLevel or 0) >= e.level);
        if (main) or (sub and not e.mainOnly) then addEffect(e.set); end
    end
    for _, s in ipairs(okSpells and known or {}) do
        if (s.Skill == 'Enhancing Magic') and ((string.find(s.Name, '^En%a+$')) or (string.find(s.Name, '^En%a+ II$'))) then addEffect('Enspell'); end
        if (string.find(s.Name, 'helix')) then addEffect('Helix'); end
        if (s.Skill == 'Dark Magic') and (string.find(s.Name, '^Absorb')) then addEffect('Absorb'); end
    end

    -- every cap less what the job's traits and gifts already give
    local bonus = plan.Bonus(who);
    for _, o in ipairs(out) do lowerCaps(o.Mode, bonus); end

    -- weapon drawn for the fighting and weapon skill sets, sheathed for the standing ones, so gear
    -- that works only one way counts only there (vg\score.lua latents)
    local standing = { Idle = true, Idle_Regen = true, Idle_Refresh = true, Resting = true, Movement = true };
    for _, o in ipairs(out) do
        if (string.sub(o.Name, 1, 3) == 'Tp_') or (string.sub(o.Name, 1, 3) == 'Ws_') or (o.Weaponskill ~= nil) then
            o.Mode.Drawn = true;
        elseif (standing[o.Name]) then
            o.Mode.Drawn = false;
        end
    end

    return out, held;
end

local skillIds = { [33] = 'Divine Magic', [34] = 'Healing Magic', [35] = 'Enhancing Magic', [36] = 'Enfeebling Magic',
    [37] = 'Elemental Magic', [38] = 'Dark Magic', [40] = 'Ninjutsu', [41] = 'Singing', [44] = 'Blue Magic',
    [45] = 'Geomancy' };

-- The spells the player has learned and can cast now: { Name, Skill (LuAshitacast's name) }.
plan.Spells = function(who)
    local resources = AshitaCore:GetResourceManager();
    local player = AshitaCore:GetMemoryManager():GetPlayer();
    if (resources.GetSpellById == nil) or (player.HasSpell == nil) then
        return {};
    end
    local list, seen = {}, {};
    for id = 1, 1023 do
        local s = resources:GetSpellById(id);
        if (s ~= nil) and (skillIds[s.Skill] ~= nil) and (player:HasSpell(id)) then
            local needMain = s.LevelRequired and s.LevelRequired[who.Job + 1] or -1;
            local needSub = (who.Sub ~= nil) and (who.Sub > 0) and s.LevelRequired and s.LevelRequired[who.Sub + 1] or -1;
            -- LevelRequired is by job id, 1-based in Lua (the job id + 1); -1 where the job has none
            local ok = ((needMain ~= nil) and (needMain > 0) and (needMain <= who.Level))
                or ((needSub ~= nil) and (needSub > 0) and (needSub <= (who.SubLevel or 0)));
            local name = s.Name and s.Name[1] or nil;
            if (ok) and (name ~= nil) and (name ~= '') and (not seen[name]) then
                seen[name] = true;
                list[#list + 1] = { Name = name, Skill = skillIds[s.Skill] };
            end
        end
    end
    table.sort(list, function(a, b) return a.Name < b.Name; end);
    return list;
end

-- Ninjutsu by what it does (Kincard's NIN guide: magic attack for the elemental ones, magic
-- accuracy for the debuffs, fast cast and haste for Utsusemi's recast).
local ninDebuff = { kurayami = true, hojo = true, jubaku = true, dokumori = true, aisha = true, yurin = true, sayonara = true };
local ninNuke = { katon = true, hyoton = true, huton = true, doton = true, raiton = true, suiton = true };

-- The mode a spell's own set is built from, or nil for a spell that gets no set of its own.
plan.SpellMode = function(skill, name)
    local first = string.lower(string.match(name, '^[%a]+') or '');
    if (skill == 'Ninjutsu') then
        if (first == 'utsusemi') then return 'Utsusemi'; end
        if (ninNuke[first]) then return 'Ninjutsu'; end
        if (ninDebuff[first]) then return 'NinjutsuDebuff'; end
        return 'NinjutsuBuff';
    elseif (skill == 'Healing Magic') then
        if (first == 'cure') or (first == 'cura') or (first == 'curaga') then return 'Cure'; end
        if (name == 'Cursna') then return 'Cursna'; end
        return nil;
    elseif (skill == 'Enhancing Magic') then
        if (first == 'stoneskin') then return 'Stoneskin'; end
        if (first == 'phalanx') then return 'Phalanx'; end
        if (first == 'regen') then return 'Regen'; end
        if (first == 'refresh') then return 'Refresh'; end
        return 'Enhancing';
    elseif (skill == 'Enfeebling Magic') then
        return 'Enfeebling';
    elseif (skill == 'Elemental Magic') then
        return 'Nuke';
    elseif (skill == 'Dark Magic') then
        if (first == 'drain') or (first == 'aspir') then return 'Drain'; end
        return 'Dark';
    elseif (skill == 'Divine Magic') then
        return 'Divine';
    elseif (skill == 'Singing') then
        -- the songs sung at the monster want to land; the rest want potency and duration
        local lowered = string.lower(name);
        for _, word in ipairs({ 'elegy', 'lullaby', 'requiem', 'threnody', 'finale', 'nocturne', 'virelai', 'magic finale', 'pining nocturne' }) do
            if (string.find(lowered, word, 1, true)) then return 'SongDebuff'; end
        end
        return 'Song';
    elseif (skill == 'Blue Magic') then
        -- by what the spell needs, as GetAwayCoxn's BLU.lua sorts them
        local L = require('vg.lists');
        if (name == 'White Wind') then return 'WhiteWind'; end
        if (L.BlueCure[name]) then return 'Cure'; end
        if (L.BlueSkill[name]) then return 'BlueSkill'; end
        if (L.BlueDebuff[name]) then return 'BlueDebuff'; end
        if (L.BlueBuff[name]) then return 'BlueBuff'; end
        if (L.BlueEnmity[name]) then return 'Enmity'; end
        return L.BluePhysical[name] and 'BluePhysical' or 'BlueMagical';
    elseif (skill == 'Geomancy') then
        if (string.find(name, '^Indi%-')) then return 'Indi'; end
        return 'Geomancy';
    end
    return nil;
end

return plan;
