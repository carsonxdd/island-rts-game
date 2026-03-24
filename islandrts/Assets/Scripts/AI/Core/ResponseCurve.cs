using UnityEngine;

/// <summary>
/// Zero-GC struct that maps a 0-1 input to a 0-1 output using various curve shapes.
/// Used by Considerations to shape raw scores into useful utility values.
/// </summary>
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
    public float slope;
    public float exponent;
    public float xShift;
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
