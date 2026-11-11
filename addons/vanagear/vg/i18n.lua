-- VanaGear UI localization. Catalogs and UI text are UTF-8; game chat is CP932.
-- Display choices never change Ashita, xiloader, item IDs or saved profile keys.
local locale = {};
local catalogs = {};
local order = {};

-- Compare only format specifiers, not letters in the surrounding prose.
local function signature(s)
    local out = {};
    s:gsub('%%%%', ''):gsub('%%[-+ #0]*%d*%.?%d*[cdiouxXeEfgGqs]', function(v) out[#out + 1] = v:sub(-1); end);
    return table.concat(out, ',');
end

local function escape(s) return (s:gsub('([%^%$%(%)%%%.%[%]%*%+%-%?])', '%%%1')); end
local function template(key)
    local out, at, count = {}, 1, 0;
    while true do
        local a, b = key:find('%%[-+ #0]*%d*%.?%d*[cdiouxXeEfgGqs]', at);
        if not a then break; end
        out[#out + 1] = escape(key:sub(at, a - 1));
        out[#out + 1] = '(.-)';
        count = count + 1;
        at = b + 1;
    end
    out[#out + 1] = escape(key:sub(at));
    return '^' .. table.concat(out) .. '$', count;
end

function locale.Register(code, catalog)
    assert(type(code) == 'string' and code:match('^[%a][%w%-]*$'), 'Invalid locale code');
    code = code:lower();
    assert(type(catalog) == 'table' and type(catalog.name) == 'string' and type(catalog.strings) == 'table', 'Invalid catalog');
    local patterns = {};
    for key, value in pairs(catalog.strings) do
        assert(type(key) == 'string' and type(value) == 'string', 'Catalog text must be strings');
        assert(signature(key) == signature(value), 'Format placeholders differ: ' .. key);
        local pattern, n = template(key);
        if n > 0 and key:find('%a%a%a') then
            patterns[#patterns + 1] = { pattern = pattern, value = value, key = key };
        end
    end
    table.sort(patterns, function(a, b) return #a.key > #b.key or (#a.key == #b.key and a.key < b.key); end);
    if not catalogs[code] then order[#order + 1] = code; end
    catalogs[code] = { name = catalog.name, strings = catalog.strings, patterns = patterns, cache = {}, cached = 0 };
end

locale.Register('en', require('vg.locales.en'));
locale.Register('ja', require('vg.locales.ja'));

function locale.Detected()
    local ok, value = pcall(function()
        return AshitaCore:GetConfigurationManager():GetInt32('boot', 'ashita.language', 'ashita', 2);
    end);
    return (ok and value == 1) and 'ja' or 'en';
end

function locale.Preference()
    local value = gVana and gVana.Prefs and gVana.Prefs.Language;
    return (value == 'auto' or catalogs[value]) and value or 'auto';
end

function locale.Current()
    local value = locale.Preference();
    return value == 'auto' and locale.Detected() or value;
end

function locale.Set(code)
    if type(code) ~= 'string' then return false; end
    code = code:lower();
    if code ~= 'auto' and not catalogs[code] then return false; end
    if not (gVana and gVana.Prefs) then return false; end
    gVana.Prefs.Language = code;
    return true;
end

function locale.Choices()
    local out = { { 'auto', 'Auto / 自動' } };
    for _, code in ipairs(order) do out[#out + 1] = { code, catalogs[code].name }; end
    return out;
end

function locale.CommandOptions()
    local codes = { 'auto' };
    for _, code in ipairs(order) do codes[#codes + 1] = code; end
    return table.concat(codes, ' | ');
end

function locale.Text(text, code)
    if type(text) ~= 'string' then return text; end
    code = code or locale.Current();
    local c = catalogs[code] or catalogs.en;
    if c.strings[text] then return c.strings[text]; end
    if code == 'en' then return text; end
    if c.cache[text] then return c.cache[text]; end
    local function remember(value)
        -- Names, scores and timestamps vary. Keep render-time work small without
        -- accumulating an unlimited cache of player-provided strings.
        if c.cached >= 256 then c.cache = {}; c.cached = 0; end
        c.cache[text] = value;
        c.cached = c.cached + 1;
        return value;
    end
    for _, entry in ipairs(c.patterns) do
        local values = { text:match(entry.pattern) };
        if #values > 0 then
            local i = 0;
            return remember((entry.value:gsub('%%[-+ #0]*%d*%.?%d*[cdiouxXeEfgGqs]', function()
                i = i + 1;
                return values[i];
            end)));
        end
    end
    local before, inner, after = text:match('^(%s*)(.-)(%s*)$');
    if inner and inner ~= text then
        local translated = locale.Text(inner, code);
        if translated ~= inner then return remember(before .. translated .. after); end
    end
    return remember(catalogs.en.strings[text] or text);
end

function locale.Format(key, ...) return string.format(locale.Text(key), ...); end

function locale.ChatText(text)
    -- FFXI's chat cannot represent arbitrary Unicode. Future UI languages keep
    -- English chat until a verified transport for their script is supplied.
    if locale.Current() ~= 'ja' then return locale.Text(text, 'en'); end
    -- Legacy callers mix game-chat CP932 with strings already decoded to UTF-8.
    -- A composed byte string has lost that provenance. Preserve it unchanged
    -- rather than guessing and double-encoding a Japanese command/set name.
    -- Producers with known UTF-8 values can explicitly use ChatFormat below.
    if type(text) ~= 'string' or text:find('[\128-\255]') then return text; end
    local translated = locale.Text(text, 'ja');
    if translated == text then return text; end
    local ok, result = pcall(function() return require('encoding'):UTF8_To_ShiftJIS(translated); end);
    return (ok and result) or text;
end

-- Explicit contract: the source key and every string argument are UTF-8.
-- The result is ready for print/chat, not for an ImGui window.
function locale.ChatFormat(key, ...)
    local code = locale.Current() == 'ja' and 'ja' or 'en';
    local original = string.format(locale.Text(key, 'en'), ...);
    local translated = string.format(locale.Text(key, code), ...);
    local ok, result = pcall(function() return require('encoding'):UTF8_To_ShiftJIS(translated); end);
    return (ok and result) or original;
end

function locale.Label(label)
    if type(label) ~= 'string' then return label; end
    local visible = label:match('^(.-)##') or label;
    if visible == '' then return label; end
    local translated = locale.Text(visible);
    -- ### itself participates in ImGui's hash, so both languages must use
    -- the same canonical suffix, including when the visible text is English.
    local explicit = label:match('###(.*)$');
    return translated .. '###' .. (explicit or label);
end

-- Local proxies: never replace package.loaded.imgui, chat, or another addon's API.
function locale.ImGui(api)
    local labels = { Button=true, SmallButton=true, Checkbox=true, RadioButton=true,
        Selectable=true, BeginTabItem=true, Begin=true, BeginCombo=true, InputText=true,
        InputInt=true, SliderInt=true, SliderFloat=true, CollapsingHeader=true };
    local texts = { Text=true, TextWrapped=true, TextDisabled=true, BulletText=true, SetTooltip=true, ShowHelp=true };
    return setmetatable({}, { __index = function(_, key)
        local fn = api[key];
        if type(fn) ~= 'function' then return fn; end
        if key == 'BeginCombo' then
            return function(label, preview, ...) return fn(locale.Label(label), locale.Text(preview), ...); end;
        end
        if labels[key] then return function(label, ...) return fn(locale.Label(label), ...); end; end
        if texts[key] then return function(text, ...) return fn(locale.Text(text), ...); end; end
        if key == 'TextColored' then return function(color, text, ...) return fn(color, locale.Text(text), ...); end; end
        return fn;
    end });
end

function locale.Style(api)
    return setmetatable({}, { __index = function(_, key)
        local fn = api[key];
        if type(fn) ~= 'function' then return fn; end
        if key == 'Button' or key == 'SmallButton' or key == 'Selectable' or key == 'Begin' then
            return function(label, ...) return fn(locale.Label(label), ...); end;
        end
        if key == 'Eyebrow' or key == 'Status' or key == 'Muted' then return function(text, ...) return fn(locale.Text(text), ...); end; end
        return fn;
    end });
end

function locale.Chat(api)
    return setmetatable({}, { __index = function(_, key)
        local fn = api[key];
        if key == 'message' or key == 'error' or key == 'warning' then
            return function(text, ...) return fn(locale.ChatText(text), ...); end;
        end
        return fn;
    end });
end

return locale;
