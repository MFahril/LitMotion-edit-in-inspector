using UnityEngine;

namespace LitMotion.TweenEditor
{
    /// <summary>
    /// The per-step parameters a channel needs beyond its <see cref="TweenChannelKey"/>.
    /// </summary>
    /// <remarks>
    /// Most channels are fully identified by their key: there is only one
    /// <see cref="TweenChannelKey.LocalPosition"/> on a Transform. The text and material
    /// channels are not -- "a float on this material" needs a property id, and "reveal this
    /// text" needs to know whether the unit is characters, words or lines. Carrying that in a
    /// small struct alongside the key keeps one read/write table rather than a second,
    /// separately maintained one for the parameterized channels.
    ///
    /// It is captured in <see cref="TweenValueSnapshot"/> too, because restoring a material
    /// property requires knowing which property was written.
    /// </remarks>
    internal struct TweenChannelContext
    {
        /// <summary>Shader property id, for the material channels.</summary>
        public int PropertyId;

        /// <summary>Character index, for the per-character TMP channels.</summary>
        public int CharIndex;

        /// <summary>Unit revealed by a <see cref="TweenType.TextReveal"/> step.</summary>
        public TweenTextUnit TextUnit;

        /// <summary>Format string applied by a <see cref="TweenType.TextCounter"/> step.</summary>
        public string Format;

        /// <summary>The user-defined channel, for <see cref="TweenChannelKey.Extension"/>.</summary>
        public ITweenExtensionChannel Extension;

        /// <summary>Builds the context a step's channel needs.</summary>
        public static TweenChannelContext For(TweenStep step)
        {
            if (step == null) return default;

            return new TweenChannelContext
            {
                PropertyId = string.IsNullOrEmpty(step.PropertyName)
                    ? 0
                    : Shader.PropertyToID(step.PropertyName),
                CharIndex = step.CharacterIndex,
                TextUnit = step.TextUnit,
                Format = string.IsNullOrEmpty(step.TextFormat) ? "{0}" : step.TextFormat,
                Extension = step.Type == TweenType.Extension
                    ? TweenExtensionRegistry.Find(step.ExtensionId)
                    : null,
            };
        }
    }
}
