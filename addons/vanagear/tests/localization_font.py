"""Check Japanese catalog glyphs and CP932 conversion, without launching FFXI.

Requires lupa and fonttools. The native converter portion runs on Windows.
"""
import os
from pathlib import Path
from fontTools.ttLib import TTFont
from lupa import luajit21

ADDON = Path(__file__).resolve().parent.parent
L = luajit21.LuaRuntime(unpack_returned_tuples=True)
L.execute("package.path = ... .. '/?.lua;' .. package.path", str(ADDON))
L.execute('function T(t) return t end')
catalog = L.eval("require('vg.locales.ja').strings")
for value in catalog.values():
    value.encode('cp932')  # Japanese translations must not silently become '?' in chat.
font = TTFont(ADDON / 'fonts' / 'MPLUS1p-Regular.ttf')
glyphs = font.getBestCmap()
needed = {ord(c) for value in catalog.values() for c in value if ord(c) > 127}
missing = needed - glyphs.keys()
assert not missing, f'Japanese font lacks: {sorted(missing)}'
print(f'PASS: bundled font covers all {len(needed)} non-ASCII catalog characters')
if os.name == 'nt':
    result = L.execute(r'''
        gVana = { Prefs = { Language = 'ja' } }
        local text = require('vg.i18n').ChatText('Settings')
        return (text:gsub('.', function(c) return string.format('%02x', c:byte()) end))
    ''')
    assert result == '設定'.encode('cp932').hex(), result
    result = L.execute(r'''
        local text = require('vg.i18n').ChatFormat('No set called %s.', 'ケアル')
        return (text:gsub('.', function(c) return string.format('%02x', c:byte()) end))
    ''')
    assert result == 'ケアル というセットはありません。'.encode('cp932').hex(), result
    result = L.execute(r'''
        local locale = require('vg.i18n')
        locale.Set('en')
        local text = locale.ChatFormat('No set called %s.', 'ケアル')
        return (text:gsub('.', function(c) return string.format('%02x', c:byte()) end))
    ''')
    assert result == 'No set called ケアル.'.encode('cp932').hex(), result
    print('PASS: actual Windows CP932 output matches Python codec')

# The font is loaded once; stack calls only bracket this addon's windows.
L.execute(r'''
    local loads, pushes, pops = 0, 0, 0
    addon = { path = ... }
    package.loaded.imgui = {
        AddFontFromFileTTF = function(path, size)
            assert(path:find('MPLUS1p%-Regular.ttf$'))
            loads = loads + 1
            return 'font-handle'
        end,
        PushFont = function(handle, size) assert(handle == 'font-handle'); pushes = pushes + 1 end,
        PopFont = function() pops = pops + 1 end,
    }
    gVana = { Prefs = { Language = 'ja' } }
    local font = require('vg.locale_font')
    assert(font.Load() and font.Load() and loads == 1)
    local pushed = font.Push(); assert(pushed); font.Pop(pushed)
    assert(pushes == 1 and pops == 1)
    gVana.Prefs.Language = 'en'
    assert(not font.Push()); font.Pop(false)
    assert(pushes == pops)
''', str(ADDON))
print('PASS: font load is idempotent and the render stack is balanced')
