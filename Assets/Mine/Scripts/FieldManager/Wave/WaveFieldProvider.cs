namespace Mine.Fields
{
    /// <summary>
    /// Wave channel base: implementations publish periodic displacement, normal and foam through
    /// the shared wave globals. One active Wave provider is allowed at a time.
    /// </summary>
    public abstract class WaveFieldProvider : FieldProvider
    {
        public sealed override string FieldKey => WaveFieldBindings.Key;

        public sealed override int FieldOrder => -100;

        public override void ResetGlobals() => WaveFieldBindings.Instance.BindNeutralGlobals();
    }
}
