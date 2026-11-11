-- Bundled OFL Japanese font, with Windows fallbacks. No font installation required.
local locale = require('vg.i18n');
local font = {};
local handle;
local tried = false;
local size = 16;

function font.Load()
    if tried then return handle ~= nil; end
    tried = true;
    local imgui = require('imgui');
    if type(imgui.AddFontFromFileTTF) ~= 'function' then return false; end
    local root = (os.getenv('WINDIR') or 'C:\\Windows') .. '\\Fonts\\';
    local paths = {};
    if addon and addon.path then
        paths[#paths + 1] = addon.path .. '/fonts/MPLUS1p-Regular.ttf';
    elseif AshitaCore and AshitaCore.GetInstallPath then
        paths[#paths + 1] = AshitaCore:GetInstallPath() .. 'addons\\vanagear\\fonts\\MPLUS1p-Regular.ttf';
    end
    for _, name in ipairs({ 'msgothic.ttc', 'meiryo.ttc', 'YuGothR.ttc' }) do paths[#paths + 1] = root .. name; end
    for _, path in ipairs(paths) do
        local f = io.open(path, 'rb');
        if f then
            f:close();
            local ok, result = pcall(imgui.AddFontFromFileTTF, path, size);
            if ok and result then handle = result; return true; end
        end
    end
    return false;
end

function font.Push()
    if not handle or (locale.Current() ~= 'ja' and locale.Detected() ~= 'ja') then return false; end
    local ok = pcall(require('imgui').PushFont, handle, size);
    return ok;
end

function font.Pop(pushed)
    if pushed then pcall(require('imgui').PopFont); end
end

function font.Available() return handle ~= nil; end
return font;
