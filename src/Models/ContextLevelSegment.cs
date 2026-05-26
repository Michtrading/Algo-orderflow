namespace AlgoOrderflow.Models
{
    /// <summary>Segment horizontal affiché sur le chart (ONH/ONL figés à l'ouverture RTH).</summary>
    public class ContextLevelSegment
    {
        public ContextLevelKind Kind { get; set; }
        public decimal Price { get; set; }
        public int FromBar { get; set; }
        /// <summary>-1 = prolongé jusqu'à la dernière bougie visible.</summary>
        public int ToBar { get; set; } = -1;
    }
}
