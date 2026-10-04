using UnityEngine;
using Object = UnityEngine.Object;

namespace LitMotion.TweenEditor
{
    /// <summary>
    /// The bind state handed to LitMotion: holds where to write, so the bound delegate can stay
    /// static and allocation-free.
    /// </summary>
    /// <remarks>
    /// LitMotion's <c>Bind&lt;TState&gt;(TState, Action&lt;TValue, TState&gt;)</c> overload exists
    /// precisely so callers can avoid a capturing closure per motion. Passing one of these as the
    /// state and a <c>static</c> lambda as the action means the only allocation is this object,
    /// once per step per play, with zero per-frame garbage.
    /// </remarks>
    internal sealed class TweenChannelWriter
    {
        public TweenChannelKey Key;
        public Object Target;
        public TweenAxis Axis = TweenAxis.All;
        public TweenChannelContext Context;

        public void WriteFloat(float value)
        {
            TweenChannelAccessor.TryWrite(Key, Target, new Vector4(value, 0f, 0f, 0f), Axis, Context);
        }

        public void WriteVector2(Vector2 value)
        {
            TweenChannelAccessor.TryWrite(Key, Target, new Vector4(value.x, value.y, 0f, 0f), Axis, Context);
        }

        public void WriteVector3(Vector3 value)
        {
            TweenChannelAccessor.TryWrite(Key, Target, new Vector4(value.x, value.y, value.z, 0f), Axis, Context);
        }

        public void WriteVector4(Vector4 value)
        {
            TweenChannelAccessor.TryWrite(Key, Target, value, Axis, Context);
        }
    }
}
