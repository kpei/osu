
using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.Distributions;

namespace osu.Game.Rulesets.Osu.Difficulty
{

    public class MissProbability {

        public readonly double mu;
        public readonly double sigma;
        public readonly double v;

        public MissProbability(double mu, double sigma, double[] coefs) {

        }
        public MissProbability(IList<double> hitProbabilities)
        {
            IEnumerable<double> missProbabilites = hitProbabilities.Select(p => 1 - p);
            mu = missProbabilites.Sum();

            double variance = 0;
            double gamma = 0;

            foreach (double p in missProbabilites)
            {
                variance += p * (1 - p);
                gamma += p * (1 - p) * (1 - 2 * p);
            }

            sigma = Math.Sqrt(variance);

            v = gamma / (6 * Math.Pow(sigma, 3));
        }

        public double chanceOf(double misses, double precision = 1e-6) {
            return (chanceOfAtMost(misses + precision) - chanceOfAtMost(misses)) / precision;
        }

        public double chanceOfAtMost(double misses)
        {
            if (sigma == 0)
                return 1;

            double k = (misses + 0.5 - mu) / sigma;

            // see equation (14) of the cited paper
            double result = Normal.CDF(0, 1, k) + v * (1 - k * k) * Normal.PDF(0, 1, k);

            if (result < 0) return 0;
            if (result > 1) return 1;

            return result;
        }

    }

}