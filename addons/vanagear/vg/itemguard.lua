-- Protect the physical piece being used, independently of profile action timeouts.
-- Slots here are LuAshitacast indices (1-16); bag/index identifies one copy of an item.
local guard = { Safety = 2 };
local holds = {};
local pending = nil;
local grace = 10; -- bounded recovery if the server never answers /item

local function current(slot)
    local h = holds[slot];
    if h ~= nil and os.clock() >= h.untilAt then
        holds[slot] = nil;
        if pending == h then pending = nil; end
        return nil;
    end
    return h;
end

guard.Get = current;

guard.Prepare = function(slot, piece)
    if current(slot) ~= nil then return nil; end
    local timing = piece.Server.use;
    local h = { slot = slot, id = piece.Id, bag = piece.Bag, index = piece.Index,
        cast = timing.act or 8, phase = 'equip',
        untilAt = os.clock() + timing.delay + guard.Safety + (timing.act or 8) + grace + guard.Safety };
    holds[slot] = h;
    return h;
end

guard.Active = function(h)
    return h ~= nil and current(h.slot) == h;
end

guard.Begin = function(bag, index, item, cast, target)
    if item == nil or item.Id == 0 or item.Count == 0 then return nil; end
    local server = require('vg.data').Items()[item.Id];
    if (server == nil or server.use == nil) and (cast or 0) <= 0 then return nil; end
    local inventory = AshitaCore:GetMemoryManager():GetInventory();
    for slot = 1, 16 do
        local currentEquip = gEquip and gEquip.GetCurrentEquip and gEquip.GetCurrentEquip(slot);
        local equipped = inventory:GetEquippedItem(slot - 1);
        local packed = equipped and equipped.Index or 0;
        local equippedIndex = currentEquip and currentEquip.Item and currentEquip.Item.Index or bit.band(packed, 0xFF);
        local equippedBag = currentEquip and currentEquip.Item and currentEquip.Container or bit.rshift(packed, 8);
        if equippedIndex == index and equippedBag == bag then
            local h = current(slot);
            if h == nil then
                h = { slot = slot, id = item.Id, bag = bag, index = index };
                holds[slot] = h;
            elseif h.id ~= item.Id or h.bag ~= bag or h.index ~= index then
                return nil;
            end
            -- Duplicated outgoing packets cannot extend a running or completed use.
            if h.phase == nil or h.phase == 'equip' then
                h.cast = math.max(cast or 0, (server and server.use and server.use.act) or 0, 1);
                h.phase = 'sent';
                h.target = target;
                h.untilAt = os.clock() + h.cast + grace + guard.Safety;
                h.hardUntil = h.untilAt + 30;
                pending = h;
            end
            return h;
        end
    end
    return nil; -- consumables do not lock an equipment slot
end

guard.Started = function(id, target)
    local h = pending;
    if guard.Active(h) and h.phase == 'sent' and h.id == id and h.target == target then
        h.phase = 'active';
        h.untilAt = math.min(h.hardUntil, os.clock() + h.cast + grace + guard.Safety);
    end
end

guard.Finished = function(id, target)
    local h = pending;
    if guard.Active(h) and h.id == id and h.target == target and h.phase ~= 'settle' then
        h.phase = 'settle';
        h.untilAt = os.clock() + guard.Safety;
    end
end

guard.Interrupted = function(target)
    local h = pending;
    if guard.Active(h) and h.target == target and h.phase ~= 'settle' then
        h.phase = 'settle';
        h.untilAt = os.clock() + guard.Safety;
    end
end

guard.Allows = function(slot, bag, index)
    local own = current(slot);
    if own ~= nil and (own.bag ~= bag or own.index ~= index) then return false; end
    for other = 1, 16 do
        local h = current(other);
        if h ~= nil and other ~= slot then
            if h.bag == bag and h.index == index then return false; end
            -- A weapon/ranged change can implicitly remove its paired offhand/ammo.
            if slot <= 4 and math.floor((slot - 1) / 2) == math.floor((other - 1) / 2) then return false; end
        end
    end
    return true;
end

guard.Reset = function()
    holds = {};
    pending = nil;
end

return guard;
