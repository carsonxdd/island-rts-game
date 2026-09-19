using UnityEngine;

/// <summary>
/// Zero-GC struct that maps a 0-1 input to a 0-1 output using various curve shapes.
/// Used by Considerations to shape raw scores into useful utility values.
/// </summary>
/// <remarks>
/// Two shapes carry gotchas worth knowing before picking one:
/// - Constant ignores its input entirely, so pairing it with a scanning consideration
///   burns the whole scan for nothing. Use ConstantScore for a fixed utility floor.
/// - A yShift above 0 means the curve can never return 0, which defeats AIBrain's
///   early-out and lets momentum keep a dead action alive. Leave it at 0 on any
///   consideration that is meant to be able to veto its action.
/// </remarks>
[System.Serializable]
public struct ResponseCurve
{
    public enum CurveType
    {
        Linear,          // y = slope * x + yShift
        InverseLinear,   // y = slope * (1 - x) + yShift
        Exponential,     // y = x^exponent
        Logistic,        // y = 1 / (1 + e^(-slope*(x-xShift)))
        Constant         // y = yShift (always returns a fixed value)
    }

    public CurveType type;
    /// <summary>Steepness. Linear/InverseLinear multiplier; Logistic sharpness.</summary>
    public float slope;
    /// <summary>Exponent for Exponential. Above 1 biases low, below 1 biases high.</summary>
    public float exponent;
    /// <summary>Logistic midpoint: the input value where the output crosses 0.5.</summary>
    public float xShift;
    /// <summary>Vertical offset (and the value itself for Constant). See the remarks above.</summary>
    public float yShift;

    public static ResponseCurve Linear(float slope = 1f, float yShift = 0f)
    {
        return new ResponseCurve { type = CurveType.Linear, slope = slope, yShift = yShift };
    }

    public static ResponseCurve InverseLinear(float slope = 1f, float yShift = 0f)
    {
        return new ResponseCurve { type = CurveType.InverseLinear, slope = slope, yShift = yShift };
    }

    public static ResponseCurve Exponential(float exponent = 2f)
    {
        return new ResponseCurve { type = CurveType.Exponential, exponent = exponent };
    }

    public static ResponseCurve Logistic(float slope = 10f, float xShift = 0.5f)
    {
        return new ResponseCurve { type = CurveType.Logistic, slope = slope, xShift = xShift };
    }

    public static ResponseCurve Constant(float value)
    {
        return new ResponseCurve { type = CurveType.Constant, yShift = value };
    }

    /// <summary>Shapes a raw 0-1 input into a 0-1 utility value. Always clamped to 0-1.</summary>
    public float Evaluate(float x)
    {
        float result;
        switch (type)
        {
            case CurveType.Linear:
                result = slope * x + yShift;
                break;
            case CurveType.InverseLinear:
                result = slope * (1f - x) + yShift;
                break;
            case CurveType.Exponential:
                result = Mathf.Pow(x, exponent);
                break;
            case CurveType.Logistic:
                result = 1f / (1f + Mathf.Exp(-slope * (x - xShift)));
                break;
            case CurveType.Constant:
                return Mathf.Clamp01(yShift);
            default:
                result = x;
                break;
        }
        return Mathf.Clamp01(result);
    }
}
