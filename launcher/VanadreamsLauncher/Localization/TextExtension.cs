using System;
using System.Windows.Markup;
using Vanadreams.Services;

namespace Vanadreams.Localization
{
    [MarkupExtensionReturnType(typeof(string))]
    public sealed class TextExtension : MarkupExtension
    {
        public string Key { get; set; }
        public override object ProvideValue(IServiceProvider serviceProvider) => UiText.FromKey(Key);
    }
}
