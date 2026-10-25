using System;

namespace MTPAD.Rehab.Trigger
{
    public sealed class CausalLowPassFilter
    {
        private bool initialized;
        private double z1;
        private double z2;

        public void Reset()
        {
            initialized = false;
            z1 = 0d;
            z2 = 0d;
        }

        public float Process(float input, double deltaTime, float requestedCutoffHz)
        {
            double sampleRate = 1d / Math.Max(0.0001d, deltaTime);
            double cutoff = Math.Max(0.01d, Math.Min(requestedCutoffHz, sampleRate * 0.45d));
            double k = Math.Tan(Math.PI * cutoff / sampleRate);
            double norm = 1d / (1d + Math.Sqrt(2d) * k + k * k);
            double b0 = k * k * norm;
            double b1 = 2d * b0;
            double b2 = b0;
            double a1 = 2d * (k * k - 1d) * norm;
            double a2 = (1d - Math.Sqrt(2d) * k + k * k) * norm;

            if (!initialized)
            {
                initialized = true;
                z1 = input * (1d - b0);
                z2 = input * (b2 - a2);
                return input;
            }

            double output = b0 * input + z1;
            z1 = b1 * input - a1 * output + z2;
            z2 = b2 * input - a2 * output;
            return (float)output;
        }
    }
}
