-- UTF-8 UI strings. Keep printf placeholders and command words intact.
local strings = {
    ['Settings'] = '設定',
    ['Your sets'] = '装備セット',
    ['New set'] = '新規セット',
    ['Built %d sets.'] = 'セットを%d個作成しました。',
};
for _, part in ipairs({ 'ja_ui', 'ja_help', 'ja_messages' }) do
    for key, value in pairs(require('vg.locales.' .. part)) do
        assert(strings[key] == nil, 'Duplicate Japanese translation: ' .. key);
        strings[key] = value;
    end
end
return { name = '日本語 / Japanese', strings = strings };
