using System.Globalization;

namespace AutoTest.Testing.Authoring;

/// <summary>
/// Closed, deterministic comparison language for official Mock If/When/Assert steps.
/// The existing "equals" property remains the right-hand literal for compatibility.
/// No user code, scripting or hardware expressions are evaluated.
/// </summary>
public static class StepConditionEvaluator
{
    public readonly record struct Result(bool Comparable, bool Matched, string? Diagnostic);

    // Optional properties: operator = == | != | > | >= | < | <=,
    // valueType = String | Number | Boolean. Omitted means String equality.
    public static string? Validate(StepNode node)
    {
        if (node.Type is not ("If" or "When" or "Assert")) return null;
        var operation = Read(node, "operator", "==");
        var type = Read(node, "valueType", "String");
        if (operation is not ("==" or "!=" or ">" or ">=" or "<" or "<="))
            return $"CONDITION_OPERATOR_INVALID: unsupported operator '{operation}'";
        if (type is not ("String" or "Number" or "Boolean"))
            return $"CONDITION_TYPE_INVALID: unsupported valueType '{type}'";
        if (type != "Number" && operation is not ("==" or "!="))
            return $"CONDITION_OPERATOR_TYPE_MISMATCH: {operation} requires Number";
        if (!node.Properties.TryGetValue("equals", out var expected)) return null;
        if (type == "Number" && !TryNumber(expected, out _))
            return "CONDITION_EXPECTED_NUMBER_INVALID: equals must be an invariant-culture number";
        if (type == "Boolean" && !bool.TryParse(expected, out _))
            return "CONDITION_EXPECTED_BOOLEAN_INVALID: equals must be true or false";
        return null;
    }

    public static Result Evaluate(StepNode node, string? actual)
    {
        var configurationError = Validate(node);
        if (configurationError is not null)
            return new(false, false, configurationError);
        if (actual is null)
            return new(false, false, "MOCK_CONDITION_NO_VALUE: input was not observed");
        if (!node.Properties.TryGetValue("equals", out var expected))
            return new(false, false, "MOCK_CONDITION_EXPECTED_MISSING");
        var operation = Read(node, "operator", "==");
        var type = Read(node, "valueType", "String");
        int comparison;
        switch (type)
        {
            case "Number":
                if (!TryNumber(actual, out var left) || !TryNumber(expected, out var right))
                    return new(false, false, "MOCK_CONDITION_INVALID_NUMBER: observation is not an invariant-culture number");
                comparison = decimal.Compare(left, right);
                break;
            case "Boolean":
                if (!bool.TryParse(actual, out var leftBool) || !bool.TryParse(expected, out var rightBool))
                    return new(false, false, "MOCK_CONDITION_INVALID_BOOLEAN: observation is not true or false");
                comparison = leftBool.CompareTo(rightBool);
                break;
            default:
                comparison = StringComparer.Ordinal.Compare(actual, expected);
                break;
        }
        var matched = operation switch
        {
            "==" => comparison == 0,
            "!=" => comparison != 0,
            ">" => comparison > 0,
            ">=" => comparison >= 0,
            "<" => comparison < 0,
            "<=" => comparison <= 0,
            _ => false // Invalid operators are intercepted by Validate.
        };
        return new(true, matched, null);
    }

    public static string Describe(StepNode node) =>
        $"{Read(node, "valueType", "String")} {Read(node, "operator", "==")} {Read(node, "equals", "")}";

    private static string Read(StepNode node, string key, string fallback) =>
        node.Properties.TryGetValue(key, out var value) ? value : fallback;

    private static bool TryNumber(string text, out decimal result) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
}
