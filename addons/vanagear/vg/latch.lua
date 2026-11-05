--[[
    vanagear: a set that goes on at a share of HP or MP, held on until a further line.

    The set changes the max HP or MP when it is worn, and so moves the share back over its own line:
    off, the share is under the line, the set goes on, the max drops, the share is over the line, the
    set comes off, the max rises, the share is under again. The gear swapped on every pass while the
    player stood still (Ferrin, 5 Nov 2026: players' characters "will not stop swapping gear even when
    they are not moving"). Each such set goes on at one line and comes off only at a further one.
]]--

local latch = {};

local held = {};

-- on < off: on under `on`, off at `off` or over. on > off: on over `on`, off at `off` or under.
latch.Latch = function(key, value, on, off)
    value = value or 0;
    if (on < off) then
        if (held[key]) then held[key] = (value < off); else held[key] = (value < on); end
    else
        if (held[key]) then held[key] = (value > off); else held[key] = (value > on); end
    end
    return held[key];
end

-- the lines the sets that go on by themselves at low HP or MP go on and come off at
latch.Lines = { Idle_Regen = { 60, 90 }, Idle_Refresh = { 70, 100 }, Dt = { 40, 60 }, PetDT = { 50, 75 } };

latch.Low = function(name, value)
    local l = latch.Lines[name];
    return latch.Latch(name, value, l[1], l[2]);
end

return latch;
