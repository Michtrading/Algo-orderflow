using System.Globalization;

namespace AlgoOrderflow.Models
{
    public class ScoreSnapshot
    {
        public decimal DeltaInefficiency { get; set; }
        public decimal WeakClose { get; set; }
        public decimal VolumeSpike { get; set; }
        public decimal ProximityContext { get; set; }
        public decimal Total { get; set; }
        public SignalSide Side { get; set; }

        public string ToCsvComponents()
        {
            var inv = CultureInfo.InvariantCulture;
            return $"{DeltaInefficiency.ToString("F2", inv)};" +
                   $"{WeakClose.ToString("F2", inv)};" +
                   $"{VolumeSpike.ToString("F2", inv)};" +
                   $"{ProximityContext.ToString("F2", inv)}";
        }
    }
}
