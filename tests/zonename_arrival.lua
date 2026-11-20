local path = 'addons/Zonename/zonename.lua'
local events, objects = {}, {}
local now, isInGame, currentZone, play = 0, false, 0, {isZoning = true}
local memoryReads = 0
local settingsReloaded = false
os.clock = function() return now end
addon = {}
T = function(t) return t end
GetPlayerEntity = function() return isInGame and {} or nil end
ashita = {events = {register = function(name, _, cb) events[name] = cb end}}
AshitaCore = {
    GetMemoryManager = function() return {
        GetPlayer = function() return play end,
        GetParty = function() return { GetMemberZone = function()
            memoryReads = memoryReads + 1
            return currentZone
        end } end,
    } end,
    GetResourceManager = function() return { GetString = function(_, _, zone)
        return ({[230]='South San d Oria', [231]='North San d Oria', [232]='Port San d Oria',
           [280]='Mog Garden', [65536]='Wide zone', [777]='New Area'})[zone]
    end } end,
    GetConfigurationManager = function() return {GetInt32 = function() return 2 end} end,
}
local gdi = {Alignment={Center=0}, FontFlags={Bold=0}, get_font_available=function() return true end,
    destroy_object=function() end, destroy_interface=function() end,
    create_object=function()
        local o={}
        function o:set_text(t) self.text=t end
        function o:set_visible(v) self.visible=v end
        function o:set_opacity(a) self.alpha=a end
        table.insert(objects,o)
        return o
    end,
}
require = function(name)
    return ({
        ['common']={},
        ['chat']={header=function() return {append=function(_, t) return t end} end, message=function(t) return t end},
        ['settings']={load=function(x) return x end, register=function() end, save=function() end,
            reload=function() settingsReloaded=true end},
        ['gdifonts.include']=gdi,
        ['gdifonts.encoding']={ShiftJIS_To_UTF8=function(_, text) return text end},
        ['scaling']={window={w=1200,h=800}},
        ['regions']={{id=1,en='San d Oria'}},
        ['regionZones']={map={{230,231,232}}},
    })[name]
end
struct = {unpack=function(fmt, data, at)
    assert(fmt=='I')
    local a,b,c,d=data:byte(at, at + 3)
    return a + 256*b + 65536*c + 16777216*d
end}
local function u32(x)
    local a=x%256; x=math.floor(x/256)
    local b=x%256; x=math.floor(x/256)
    local c=x%256; x=math.floor(x/256)
    return string.char(a,b,c,x)
end
local function packet(zone, state, id)
    events.packet_in({id=id or 0x0A, data=string.rep('\0', 0x30)..u32(zone)
        ..string.rep('\0', 0x80-0x34)..u32(state or 2)})
end

dofile(path)
packet(230)
events.load()
events.d3d_present()
assert(not objects[1].visible, 'Initial login must wait for player readiness')
isInGame=true; play.isZoning=false
events.d3d_present()
assert(objects[1].visible and objects[1].text=='South San d Oria' and objects[2].text=='San d Oria')
assert(memoryReads==0, 'Never read stale party memory for automatic display')

now=5.75
events.d3d_present()
assert(objects[1].alpha < 1)
currentZone=230; play.isZoning=true
packet(231)
events.d3d_present()
assert(not objects[1].visible)
play.isZoning=false
events.d3d_present()
assert(objects[1].visible and objects[1].text=='North San d Oria' and objects[1].alpha==1)
now=6.5
packet(230)
packet(232)
events.packet_in({id=0x0A,data='short'})
packet(231, 2, 0x0B)
events.d3d_present()
assert(objects[1].text=='Port San d Oria', 'Use latest valid arrival')
packet(232,1)
events.d3d_present()
assert(not objects[1].visible, 'Do not show the outside city for its Mog House')
packet(280,1)
events.d3d_present()
assert(objects[1].text=='Mog Garden' and objects[1].visible)
packet(777)
events.d3d_present()
assert(objects[1].text=='New Area' and objects[2].text=='')
packet(65536)
events.d3d_present()
assert(objects[1].text=='Wide zone', 'Honor the full field')
packet(12345)
events.d3d_present()
assert(not objects[1].visible, 'Never display stale text for an unknown zone')
currentZone=231
local command = {command={args=function() return {'/zonename',{any=function(_, x) return x=='reload' end}} end}}
events.command(command)
events.d3d_present()
assert(command.blocked and settingsReloaded and objects[1].text=='North San d Oria' and memoryReads==1)
now=now+6.1
events.d3d_present()
assert(not objects[1].visible)
print('Zonename arrival checks passed')
