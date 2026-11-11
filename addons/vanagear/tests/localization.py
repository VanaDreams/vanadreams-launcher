"""Offline locale selection, rendering boundaries and catalog contracts (LuaJIT)."""
from pathlib import Path
from lupa import luajit21

ADDON = Path(__file__).resolve().parent.parent
L = luajit21.LuaRuntime(unpack_returned_tuples=True)
L.execute("package.path = ... .. '/?.lua;' .. package.path", str(ADDON))
L.execute(r'''
local detected = 2
AshitaCore = { GetConfigurationManager = function() return {
    GetInt32 = function(_, file, section, key, default)
        assert(file == 'boot' and section == 'ashita.language' and key == 'ashita')
        return detected
    end
} end }
gVana = { Prefs = {} }
local ok, locale = pcall(require, 'vg.i18n')
assert(ok, 'VanaGear must provide the localization module: ' .. tostring(locale))
assert(locale.Current() == 'en')
detected = 1
assert(locale.Current() == 'ja', 'Auto uses Ashita Japanese=1, never xiloader Japanese=0')
assert(locale.Set('en') and gVana.Prefs.Language == 'en')
assert(locale.Current() == 'en', 'Manual English overrides Japanese client')
detected = 2
assert(locale.Set('ja') and locale.Current() == 'ja')
assert(locale.Text('Settings') == '設定')
assert(locale.Text('Untranslated future label') == 'Untranslated future label')
assert(locale.Text('/vg wear Idle') == '/vg wear Idle', 'Commands are never translated')
assert(locale.Format('Built %d sets.', 3) == 'セットを3個作成しました。')
assert(not locale.Set('xx'), 'Unsupported choices do not overwrite saved choice')
assert(gVana.Prefs.Language == 'ja')
assert(locale.Set('auto') and locale.Current() == 'en')
detected = 0
assert(locale.Current() == 'en', 'xiloader numbering is not Ashita numbering')
detected = nil
assert(locale.Current() == 'en', 'Missing detection falls back safely')
gVana.Prefs.Language = 'bad-saved-value'
assert(locale.Current() == 'en')

locale.Register('es', { name = 'Español', strings = { Settings = 'Ajustes' } })
assert(locale.Set('es') and locale.Text('Settings') == 'Ajustes')
assert(locale.Text('Your sets') == 'Your sets', 'Incomplete new catalogs fall back to English')
local good = pcall(locale.Register, 'broken', { name = 'Broken', strings = { ['Built %d sets.'] = 'wrong %s' } })
assert(not good, 'Catalogs reject incompatible format placeholders')
assert(locale.ChatText('Settings') == 'Settings', 'Non-Shift-JIS locales use English in game chat')

locale.Set('ja')
local seen = {}
local ui = locale.ImGui({
    Button = function(label) seen.button = label; return true end,
    Text = function(text) seen.text = text end,
    TextColored = function(color, text) seen.color = color; seen.text = text end,
    InputText = function(label, value) seen.input = label; seen.value = value; return false end,
})
assert(ui.Button('Settings##settings'))
assert(seen.button == '設定###Settings##settings', 'Keep the original widget ID across language changes')
ui.Text('Settings'); assert(seen.text == '設定')
local input = { 'Settings' }
ui.InputText('New set##new', input)
assert(seen.value == input and input[1] == 'Settings', 'Never translate editable player data')
ui.TextColored({1, 1, 1, 1}, 'Settings'); assert(seen.text == '設定')
locale.Set('en'); ui.Button('Settings##settings')
assert(seen.button == 'Settings###Settings##settings', 'English uses the same canonical ID suffix')
locale.Set('ja')
ui.Button('Settings###fixed')
assert(seen.button == '設定###fixed', 'Explicit stable IDs are preserved')
assert(locale.Text('New set for Zeid') == 'Zeid の新規セット')
assert(locale.Text('Waiting after zone-in: yes, nothing is equipped yet.') == 'エリア移動後の待機：yes, nothing is equipped yet。')

package.loaded.encoding = { UTF8_To_ShiftJIS = function(_, text) return 'CP932:' .. text end }
assert(locale.ChatText('Settings') == 'CP932:設定', 'Game chat always crosses the encoding boundary')
assert(locale.ChatText('Language: ja (ja).') == 'CP932:表示言語：ja（ja）。')
assert(locale.ChatText('  open the window: your sets, the piece in each slot and why') == 'CP932:  装備セット・各部位の装備と選定結果を表示')
assert(locale.ChatText('An unrecognized engine error') == 'An unrecognized engine error')
local opaque = 'No set called ' .. string.char(0x83, 0x50, 0x83, 0x41, 0x83, 0x8B) .. '.'
assert(locale.ChatText(opaque) == opaque, 'Raw CP932 command arguments must never be treated as UTF-8')
local utf8 = 'No set called ケアル.'
assert(locale.ChatText(utf8) == utf8, 'Unknown provenance is preserved even if bytes happen to be valid UTF-8')
assert(locale.ChatFormat('No set called %s.', 'ケアル') == 'CP932:ケアル というセットはありません。')
locale.Set('en')
assert(locale.ChatFormat('No set called %s.', 'ケアル') == 'CP932:No set called ケアル.')
locale.Set('ja')

local raw = { message = function(text) return '[' .. text .. ']' end }
local localized = locale.Chat(raw)
assert(localized.message('Settings') == '[CP932:設定]')
assert(raw.message('Settings') == '[Settings]', 'Other users of the chat API are untouched')

print('PASS: detection, manual choice, fallback, extension, placeholders, chat boundary and stable UI IDs')
''')

# Exercise the actual ImGui ### hash-reset rule, not just label spelling.
import zlib


def imgui_id(label):
    data = label.encode('utf-8')
    crc = 0
    for i, byte in enumerate(data):
        if data[i:i + 3] == b'###':
            crc = 0
        crc = zlib.crc32(bytes([byte]), crc)
    return crc


label = L.eval("require('vg.i18n').Label")
set_language = L.eval("require('vg.i18n').Set")
for source in ('Settings##settings', 'New set', 'Settings###fixed', '##hidden'):
    set_language('en')
    english = label(source)
    set_language('ja')
    japanese = label(source)
    assert imgui_id(english) == imgui_id(japanese), (english, japanese)
print('PASS: English and Japanese controls have equal ImGui IDs')
