-- From LuAshitacast 2.14 by ThornyFFXI (https://github.com/ThornyFFXI/LuAshitacast), MIT licence,
-- Copyright (c) 2021 ThornyFFXI; see LICENSE-LuAshitacast. Lines marked "vanagear:" are Vanadreams changes.
local Buffer = {};
local ImmediateBuffer = {};
local EquippedItems = {};
local Internal = {};
local Fallback = nil; -- vanagear: built pieces for slots whose named piece is not carried
local CurrentJob = 0;
local CurrentSubJob = 0; -- vanagear: Vanadreams lets the main job wear its support job's gear
local CurrentLevel = 0;
local inventoryManager = AshitaCore:GetMemoryManager():GetInventory();
local resourceManager = AshitaCore:GetResourceManager();
local encoding = require('encoding');
local japanese = (AshitaCore:GetConfigurationManager():GetInt32('boot', 'ashita.language', 'ashita', 2) == 1);

local UpdateJobLevel = function()    
    CurrentJob = AshitaCore:GetMemoryManager():GetPlayer():GetMainJob();
    CurrentSubJob = AshitaCore:GetMemoryManager():GetPlayer():GetSubJob(); -- vanagear
    CurrentLevel = AshitaCore:GetMemoryManager():GetPlayer():GetJobLevel(CurrentJob);
    if (gSettings.AllowSyncEquip == false) then
        CurrentLevel = AshitaCore:GetMemoryManager():GetPlayer():GetMainJobLevel();
    end
end

--Gets current equipment based on outgoing packet history
local GetCurrentEquip = function(slot)
    if (Internal[slot] ~= nil) and (os.clock() < Internal[slot].Timer) then
        return Internal[slot];
    else
        Internal[slot] = nil;
        local equippedItem = inventoryManager:GetEquippedItem(slot - 1);
        local index = (equippedItem ~= nil) and bit.band(equippedItem.Index, 0x00FF) or 0; -- vanagear: nil while zoning
        local eqEntry = {};
        if (index == 0) then
            eqEntry.Container = 0;
            eqEntry.Item = nil;
        else
            eqEntry.Container = bit.band(equippedItem.Index, 0xFF00) / 256;
            eqEntry.Item = inventoryManager:GetContainerItem(eqEntry.Container, index);
            if (eqEntry.Item == nil) or (eqEntry.Item.Id == 0) or (eqEntry.Item.Count == 0) then -- vanagear: nil check
                eqEntry.Item = nil;
            end
        end
        return eqEntry;
    end
end

--Create new table so we aren't directly altering users tables or storing item indices
local MakeItemTable = function(item)
    local itemTable = {}
    if (type(item) == 'string') then
        itemTable.Name = string.lower(item);
    elseif (type(item) == 'table') then
        for k,v in pairs(item) do
            if (k == 'Name') then
                itemTable.Name = string.lower(v);
            elseif (k == 'Augment') then
                itemTable.Augment = v;
            elseif (k == 'Bag') then
                if (type(v) == 'string') then
                    itemTable.Bag = gData.Constants.Containers[v];
                elseif (type(v) == 'number') then
                    itemTable.Bag = v;
                end
            elseif (k == 'Priority') and (type(v) == 'number') then
                itemTable.Priority = v;
            elseif (k == 'AugPath') then
                itemTable.AugPath = v;
            elseif (k == 'AugRank') then
                itemTable.AugRank = v;
            elseif (k == 'AugTrial') then
                itemTable.AugTrial = v;
            end
        end
    else
        return nil;
    end

    --Handle special cases
    if (itemTable.Name == 'displaced') then
        itemTable.Index = -1;
    elseif (itemTable.Name == 'remove') then
        itemTable.Index = 0;
        if (itemTable.Priority == nil) then
            itemTable.Priority = -100;
        end
    end
    
    --Set priority if it's not set
    if (itemTable.Priority == nil) then
        itemTable.Priority = 0;
    end

    return itemTable;
end

--Checks if augments match
local CheckAugments = function(equipTable, item)
    local augment = gData.GetAugment(item);

    if (equipTable.AugPath ~= nil) then
        if (augment.Path ~= equipTable.AugPath) then
            return false;
        end
    end

    if (equipTable.AugRank ~= nil) then
        if (augment.Rank ~= equipTable.AugRank) then
            return false;
        end
    end

    if (equipTable.AugTrial ~= nil) then
        if (augment.Trial ~= equipTable.AugTrial) then
            return false;
        end
    end
    
    if (equipTable.Augment ~= nil) then
        if (type(equipTable.Augment) == 'string') then
            local match = false;
            if (type(augment.Augs) == 'table') then
                for _,checkAugment in pairs(augment.Augs) do
                    if (checkAugment.String == equipTable.Augment) then
                        match = true;
                        break;
                    end
                end
            end
            if (match == false) then
                return false;
            end
        elseif (type(equipTable.Augment) == 'table') then
            for _,matchAugment in pairs(equipTable.Augment) do
                local match = false;
                if (type(augment.Augs) == 'table') then
                    for _,checkAugment in pairs(augment.Augs) do
                        if (checkAugment.String == matchAugment) then
                            match = true;
                            break;
                        end
                    end
                end
                if (match == false) then
                    return false;
                end
            end
        end
    end
    
    return true;
end

--Checks if an item is already equipped to another slot it can't be removed from
local CheckEquipped = function(index, container)
    for k,v in pairs(EquippedItems) do
        if (v.Index == index) and (v.Container == container) and (v.Reserved == true) then
            return true;
        end
    end
    return false;
end

--Checks if an item is equippable for the player's current job and level
local CheckResource = function(resource)
    if (resource == nil) then
        return false;
    end

    if (bit.band(resource.Flags, 0x800) == 0) then
        return false;
    end
    
    if (CurrentLevel < resource.Level) then
        return false;
    end

    -- vanagear: the support job's gear counts too; the server says no where that is not allowed
    if (bit.band(resource.Jobs, math.pow(2, CurrentJob)) == 0) and ((CurrentSubJob == 0) or (bit.band(resource.Jobs, math.pow(2, CurrentSubJob)) == 0)) then
        return false;
    end

    return true;
end

--Checks if an item matches an equipment table
local CheckEquipTable = function(slot, container, equipTable, item, resource, resourceName)
    --Check if item name matches (lua uses pointer internally so very cheap)
    if (equipTable.Name ~= resourceName) then
        return false;
    end
    
    --Check if equip table is already satisfied
    if ((equipTable.Index ~= nil) or (gState.Disabled[slot] == true) or (gState.Encumbrance[slot] == true)) then
        return false;
    end

    --Check if item is already equipped and reserved
    if (CheckEquipped(item.Index, container)) then
        return false;
    end

    --Check if item can be equipped in desired slot
    if (bit.band(resource.Slots, math.pow(2, slot - 1)) == 0) then
        return false;
    end

    --Check that item bag matches if specified
    if (equipTable.Bag ~= nil) and (equipTable.Bag ~= container) then
        return false;
    end

    --Check that item augment matches if specified
    if (CheckAugments(equipTable, item) == false) then
        return false;
    end

    --Record item index and container because it matches everything
    equipTable.Index = item.Index;
    equipTable.Container = container;
    return true;
end

--Checks if an item should be equipped
local CheckItem = function(set, container, item)
    --Skip bazaared item (1 cheap operation)
    if (item.Flags == 19) then
        return;
    end

    --Skip item that can't be equipped by current job, or item that can't resolve resource(~5 cheap operations vs potentially 16 equip slots)
    local resource = resourceManager:GetItemById(item.Id);
    if (CheckResource(resource) == false) then
        return;
    end
    local resourceName = encoding:ShiftJIS_To_UTF8(resource.Name[1]);
    if not japanese then
        resourceName = string.lower(resourceName);
    end

    --Check if item fits any equip slots
    for slot,equipTable in pairs(set) do
        if (CheckEquipTable(slot, container, equipTable, item, resource, resourceName)) then
            return;
        end
    end
end

--Checks if an item should be equipped
local CheckItemMatch = function(container, item, equipTable)
    if (item.Id == 0) then
        return (equipTable.Name == 'remove');
    end
    local resource = resourceManager:GetItemById(item.Id);
    if (CheckResource(resource) == false) then
        return false;
    end
    local resourceName = encoding:ShiftJIS_To_UTF8(resource.Name[1]);
    if not japanese then
        resourceName = string.lower(resourceName);
    end

    --Check if item name matches (lua uses pointer internally so very cheap)
    if (equipTable.Name ~= resourceName) then
        return false;
    end

    --Check that item bag matches if specified
    if (equipTable.Bag ~= nil) and (equipTable.Bag ~= container) then
        return false;
    end

    --Check that item augment matches if specified
    if (CheckAugments(equipTable, item) ==  false) then
        return false;
    end

    return true;
end

--Clears the internal buffer for current executing state
local ClearBuffer = function()
    Buffer =  {};
    ImmediateBuffer = {};
end

--Equips an item to the internal buffer for current executing state
local EquipItemToBuffer = function(slot, itemTable, immediate)
    local targetBuffer = immediate and ImmediateBuffer or Buffer;

    if (targetBuffer[slot] ~= nil) then
        if (targetBuffer[slot].Locked == true) then
            if (itemTable.Locked ~= true) then
                return;
            end
        end
    end
    
    if (itemTable.Name == 'ignore') then
        targetBuffer[slot] = nil;
        return;
    end

    targetBuffer[slot] = itemTable;
end

--Flags equipped items.  Returns true if all items are equipped.
local FlagEquippedItems = function(set)
    EquippedItems = {};

    for i = 1,16,1 do
        local currItem = GetCurrentEquip(i);
        if (currItem.Item ~= nil) then
            local eqItem = {};
            eqItem.Container = currItem.Container;
            eqItem.Index = currItem.Item.Index;
            eqItem.Reserved = false;
            
            local equipTable = set[i];
            local resource = resourceManager:GetItemById(currItem.Item.Id);
            if (equipTable ~= nil) then
                if (CheckItemMatch(currItem.Container, currItem.Item, equipTable)) then
                    equipTable.Index = currItem.Item.Index;
                    equipTable.Container = currItem.Container;
                    equipTable.Skip = true;
                    eqItem.Reserved = true;
                elseif (gState.Encumbrance[i] == true) or (gState.Disabled[i] == true) then
                    eqItem.Reserved = true;
                end
            end
            EquippedItems[i] = eqItem;
        end
    end

    for slot,equipTable in pairs(set) do
        if (equipTable.Index == nil) then
            return false;
        elseif (equipTable.Index == 0) and (GetCurrentEquip(slot) ~= nil) then
            return false;
        end
    end

    return true;
end


--Iterates all valid items, passing them through TestItem
local LocateItems = function(set)
    for _,container in ipairs(gSettings.EquipBags) do
        local available = gData.GetContainerAvailable(container);
        if (available == true) then
            local max = gData.GetContainerMax(container);
            for index = 1,max,1 do
                local containerItem = inventoryManager:GetContainerItem(container, index);
                if containerItem ~= nil and containerItem.Count > 0 and containerItem.Id > 0 then
                    CheckItem(set, container, containerItem);
                end
            end            
        end
    end
end

--Creates an array of equipment packet info in order of priority
local PrepareEquip = function(set)    
    --Clear unused equip commands
    for i = 1,16,1 do
        if (set[i] ~= nil) then
            if (set[i].Index == nil) then
                set[i] = nil;
            elseif (set[i].Skip == true) then
                set[i] = nil;
            end
        end
    end

    -- Fill a table with the actual info to be used in the packets.
    local retTable = T{};
    for index,data in pairs(set) do
        if (data.Index ~= nil) and (data.Index ~= -1) then
            local entry = {Slot=index-1, Index=data.Index, Priority=data.Priority};
            if (entry.Index == 0) then
                local currItem = GetCurrentEquip(index);
                if (currItem.Item ~= nil) and (gState.Disabled[index] ~= true) then
                    entry.Container = currItem.Container;
                    retTable:append(entry);
                end
            else
                entry.Container = data.Container;
                retTable:append(entry);
            end
        end
    end

    -- Sort, preferring priority but falling back to slot.
    table.sort(retTable, function(a,b)
        if (a.Priority ~= b.Priority) then
            return a.Priority > b.Priority;
        else
            return a.Slot < b.Slot;
        end
    end);

    return retTable;
end

--Sends actual equip packets to equip desired set
local ProcessEquip = function(equipInfo, set)
    for _, equipEntry in ipairs(equipInfo) do
        local packet = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        packet[5] = equipEntry.Index;
        packet[6] = equipEntry.Slot;
        packet[7] = equipEntry.Container;
        if (gVanaCore ~= nil) then gVanaCore.MarkOwnEquip(); end -- vanagear: ours, not another swapper's
        AshitaCore:GetPacketManager():AddOutgoingPacket(0x50, packet);
        local internalEntry = {};
        if (equipEntry.Index == 0) then
            internalEntry.Container = 0;
            internalEntry.Item = nil;
        else
            internalEntry.Container = equipEntry.Container;
            internalEntry.Item = inventoryManager:GetContainerItem(equipEntry.Container, equipEntry.Index);
        end
        internalEntry.Timer = os.clock() + 0.2;
        Internal[equipEntry.Slot + 1] = internalEntry;
        if (gSettings.Debug) then
            if (equipEntry.Index == 0) then
                print(chat.header('LuAshitacast:' .. gState.CurrentCall) .. chat.message('Unequipping item from ' .. gData.ResolveString(gData.Constants.EquipSlotNames, equipEntry.Slot) .. '.'));
            else
                local resource = resourceManager:GetItemById(internalEntry.Item.Id);
                if (resource ~= nil) then
                    print(chat.header('LuAshitacast:' .. gState.CurrentCall) .. chat.message('Equipping ' .. resource.Name[1] .. ' to ' .. gData.ResolveString(gData.Constants.EquipSlotNames, equipEntry.Slot) .. '.'));
                else
                    print(chat.header('LuAshitacast:' .. gState.CurrentCall) .. chat.message('Equipping ItemID:' .. internalEntry.Item.Id .. ' to ' .. gData.ResolveString(gData.Constants.EquipSlotNames, equipEntry.Slot) .. '.'));
                end
            end
        end
    end
end

--Sends actual equip packets to equip desired set
local ProcessEquipSet = function(equipInfo, set)
    local packet = {};
    for i = 1,72,1 do
        packet[i] = 0x00;
    end

    local count = 1;
    for _, equipEntry in ipairs(equipInfo) do
        packet[5] = count;
        local offset = 4 + (count * 4) + 1;
        packet[offset] = equipEntry.Index;
        packet[offset + 1] = equipEntry.Slot;
        packet[offset + 2] = equipEntry.Container;
        count = count + 1;
        
        local internalEntry = {};
        if (equipEntry.Index == 0) then
            internalEntry.Container = 0;
            internalEntry.Item = nil;
        else
            internalEntry.Container = equipEntry.Container;
            internalEntry.Item = inventoryManager:GetContainerItem(equipEntry.Container, equipEntry.Index);
        end
        internalEntry.Timer = os.clock() + 0.2;
        Internal[equipEntry.Slot + 1] = internalEntry;

        if (gSettings.Debug) then
            if (equipEntry.Index == 0) then
                print(chat.header('LuAshitacast:' .. gState.CurrentCall) .. chat.message('Adding unequip to equipset for ' .. gData.ResolveString(gData.Constants.EquipSlotNames, equipEntry.Slot) .. '.'));
            else
                local resource = resourceManager:GetItemById(internalEntry.Item.Id);
                if (resource ~= nil) then
                    print(chat.header('LuAshitacast:' .. gState.CurrentCall) .. chat.message('Adding ' .. resource.Name[1] .. ' to equipset for ' .. gData.ResolveString(gData.Constants.EquipSlotNames, equipEntry.Slot) .. '.'));
                else
                    print(chat.header('LuAshitacast:' .. gState.CurrentCall) .. chat.message('Adding ItemID:' .. internalEntry.Item.Id .. ' to equipset for ' .. gData.ResolveString(gData.Constants.EquipSlotNames, equipEntry.Slot) .. '.'));
                end
            end
        end
    end
    if (gVanaCore ~= nil) then gVanaCore.MarkOwnEquip(); end -- vanagear: ours, not another swapper's
    AshitaCore:GetPacketManager():AddOutgoingPacket(0x51, packet);
end


--Sends unequip packet directly.
local Unequip = function(slot, container)
    local packet = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    packet[5] = 0;
    packet[6] = slot - 1;
    packet[7] = container;
    if (gVanaCore ~= nil) then gVanaCore.MarkOwnEquip(); end -- vanagear: ours, not another swapper's
    AshitaCore:GetPacketManager():AddOutgoingPacket(0x50, packet);
    if (gSettings.Debug) then
        print(chat.header('LuAshitacast:' .. gState.CurrentCall) .. chat.message('Unequipping item from ' .. gData.ResolveString(gData.Constants.EquipSlotNames, slot - 1) .. '.'));
    end

    local internalEntry = {};
    internalEntry.Container = 0;
    internalEntry.Item = nil;
    internalEntry.Timer = os.clock() + 0.2;
    Internal[slot] = internalEntry;
end

--Unequips item in a slot if an item is in the slot.
local UnequipSlot = function(slot)
    local current = GetCurrentEquip(slot);
    if (current.Item ~= nil) then
        Unequip(slot, current.Container);
    end
end

--Unequips anything we need to equip that's already equipped in the wrong slot.
local UnequipConflicts = function(equipInfo)
    for _, equipEntry in pairs(equipInfo) do
        for k,v in pairs(EquippedItems) do
            if (v.Index == equipEntry.Index) and (v.Container == equipEntry.Container) and (v.Reserved == false) then
                Unequip(k, v.Container);
            end
        end
    end
end


local EquipSet = function(set, style)
    UpdateJobLevel();
    if (type(set) ~= 'table') then
        return;
    end

    for i = 1,16,1 do
        if (set[i] ~= nil) and (set[i].Name == 'ignore') then
            set[i] = nil;
        end
    end

    -- vanagear: with /vg weapons off nothing from any set, built, profile or imported, touches
    -- Main, Sub or Range; only the weapons picked in the window, or named in a profile's gVana.WeaponAllow (its own
    -- /fish rod and the like), go on. In LuAshitacast mode (a profile of the player's own) the profile's weapons go on
    -- as written, the same as LuAshitacast.
    if (gVana ~= nil) and (gVana.Prefs ~= nil) and (not gVana.Prefs.Weapons) and ((gVanaCore == nil) or (not gVanaCore.PureLac())) then
        local picks = gVana.WeaponPicks or {};
        for i = 1,3,1 do
            if (set[i] ~= nil) and (not picks[set[i].Name]) and (not (gVana.WeaponAllow and gVana.WeaponAllow[set[i].Name])) then
                set[i] = nil;
            end
        end
    end

    --Flag items that are already equipped.  If all are already equipped, don't bother parsing inventory.
    if (FlagEquippedItems(set) == true) then
        return;
    end
    
    LocateItems(set);

    -- vanagear: a piece the profile names that is not in an equippable bag is noted for
    -- /vg status, and where vanagear built a piece for that slot it is worn instead
    local missing, retry = {}, {};
    for slot, equipTable in pairs(set) do
        if (equipTable.Index == nil) and (type(equipTable.Name) == 'string') then
            missing[#missing + 1] = equipTable.Name;
            if (Fallback ~= nil) and (Fallback[slot] ~= nil) then
                local copy = {};
                for k, v in pairs(Fallback[slot]) do copy[k] = v; end
                copy.Index = nil; copy.Container = nil; copy.Skip = nil;
                set[slot] = copy;
                retry[slot] = copy;
            end
        end
    end
    if (next(retry) ~= nil) then
        LocateItems(retry);
    end
    if (gVana ~= nil) then
        gVana.LastMissing = missing;
        gVana.LastEquipAt = os.clock();
    end

    --Prepare table of equip packets.
    local equipInfo = PrepareEquip(set);
    if (#equipInfo == 0) then
        return;
    end

    UnequipConflicts(equipInfo);
    
    --Send equip packets.
    if (style == 'set') then
        ProcessEquipSet(equipInfo);
    elseif (style == 'single') then
        ProcessEquip(equipInfo);
    else
        if (#equipInfo < 9) then
            ProcessEquip(equipInfo);
        else
            ProcessEquipSet(equipInfo);
        end
    end

    --Handle displaced items
    for k,v in pairs(set) do
        if (v.Index == -1) then
            local internalEntry = {};
            internalEntry.Container = 0;
            internalEntry.Item = nil;
            internalEntry.Timer = os.clock() + 0.2;
            Internal[k] = internalEntry;
        end
    end
end

--Called from state to equip the immediate set and store the buffered set in delayed struct.
local ProcessImmediateBuffer = function(style)
    --Copy buffer into delayed storage
    gState.DelayedEquip.Set = {};
    for k,v in pairs(Buffer) do
        gState.DelayedEquip.Set[k] = v;
    end

    if (gState.ForceSet ~= nil) then
        if (os.clock() > gState.ForceSetTimer) then
            gState.ForceSet = nil;
        else
            local newTable = {};
            for k,v in pairs(gState.ForceSet) do
                local equipSlot = gData.GetEquipSlot(k);
                if (equipSlot == 0) then
                    print(chat.header('vanagear') .. chat.error("Invalid slot specified: ") .. chat.color1(2, k));
                end
            
                local table = MakeItemTable(v);
                if (table ~= nil) and (type(table.Name) == 'string') then
                    newTable[equipSlot] = table;
                end
            end
            EquipSet(newTable, style);
            return;
        end
    end

    EquipSet(ImmediateBuffer, style);
end

--Called from state to equip the set player has decided on
--style can be 'single' (equip with single equip packets)  'set' (equip with equipset packet) or 'auto' (whichever is smaller)
local ProcessBuffer = function(style)
    if (gState.ForceSet ~= nil) then
        if (os.clock() > gState.ForceSetTimer) then
            gState.ForceSet = nil;
        else
            local newTable = {};
            for k,v in pairs(gState.ForceSet) do
                local equipSlot = gData.GetEquipSlot(k);
                if (equipSlot == 0) then
                    print(chat.header('vanagear') .. chat.error("Invalid slot specified: ") .. chat.color1(2, k));
                end
            
                local table = MakeItemTable(v);
                if (table ~= nil) and (type(table.Name) == 'string') then
                    newTable[equipSlot] = table;
                end
            end
            EquipSet(newTable, style);
            return;
        end
    end
    EquipSet(Buffer, style);
end

--Called with a table of slot,string.
local LockStyle = function(set)
    local packet = {};
    for i = 1,136,1 do
        packet[i] = 0x00;
    end
    packet[0x05 + 1] = 3;
    packet[0x06 + 1] = 1;

    local count = 0;
    for i = 1,9 do
        local found = false;
        local equip = set[i];
        if equip then
            if (equip == 'remove') then
                local offset = 8 + (count * 8) + 1;
                packet[offset + 1] = i - 1;
                count = count + 1;
                packet[4 + 1] = count;
            else
                for container = 0,16,1 do
                    --Only need to check access to wardrobe3/4, any other container can always be lockstyled from.
                    if container < 11 or gData.GetContainerAvailable(container) then
                        local max = gData.GetContainerMax(container);
                        for index = 1,max,1 do
                            local containerItem = inventoryManager:GetContainerItem(container, index);
                            if containerItem ~= nil and containerItem.Count > 0 and containerItem.Id > 0 then
                                local resource = resourceManager:GetItemById(containerItem.Id);
                                if (resource ~= nil) then
                                    local resourceName = encoding:ShiftJIS_To_UTF8(resource.Name[1]);
                                    if not japanese then
                                        resourceName = string.lower(resourceName);
                                    end
                                    if (resourceName == equip) and (bit.band(resource.Slots, math.pow(2, i - 1)) ~= 0) then
                                        local offset = 8 + (count * 8) + 1;
                                        packet[offset] = index;
                                        packet[offset + 1] = i - 1;
                                        packet[offset + 2] = container;
                                        packet[offset + 4] = bit.band(containerItem.Id, 0xFF);
                                        packet[offset + 5] = bit.rshift(containerItem.Id, 8);
                                        count = count + 1;
                                        packet[4 + 1] = count;
                                        found = true;
                                        break;
                                    end
                                end
                            end
                        end  
                        if found then
                            break;
                        end          
                    end
                end
            end
        end
    end

    if (count > 0) then
        gState.LockStyle = true;
        AshitaCore:GetPacketManager():AddOutgoingPacket(0x53, packet);
    end
end

-- vanagear: the built pieces to fall back on for the equip pass in progress, by slot
local SetFallback = function(set)
    Fallback = set;
end

local GetBuffer = function()
    return Buffer;
end

local exports = {
    SetFallback = SetFallback,
    GetBuffer = GetBuffer,
    CheckItemMatch = CheckItemMatch,
    ClearBuffer = ClearBuffer,
    EquipItemToBuffer = EquipItemToBuffer,
    EquipSet = EquipSet,
    GetCurrentEquip = GetCurrentEquip,
    LockStyle = LockStyle,
    MakeItemTable = MakeItemTable,
    ProcessBuffer = ProcessBuffer,
    ProcessImmediateBuffer = ProcessImmediateBuffer,
    UnequipSlot = UnequipSlot,
    UpdateJobLevel = UpdateJobLevel,
};

return exports;