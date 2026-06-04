using System;
using System.Text.Json;
using System.Collections.Generic;

class Program
{
    static void Main()
    {
        var tiersJson = "[{\"MinAmount\":10000,\"BonusRate\":5},{\"MinAmount\":20000,\"BonusRate\":10}]";
        var tiers = new List<(decimal MinAmount, decimal BonusRate)>();
        var tiersArray = JsonSerializer.Deserialize<JsonElement>(tiersJson);
        if (tiersArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in tiersArray.EnumerateArray())
            {
                bool hasRate = item.TryGetProperty("BonusRate", out var bonusProp) || item.TryGetProperty("bonusRate", out bonusProp);
                bool hasMinAmount = item.TryGetProperty("MinAmount", out var minAmtProp) || item.TryGetProperty("minAmount", out minAmtProp);
                
                if (hasRate && bonusProp.TryGetDecimal(out var rate) && hasMinAmount && minAmtProp.TryGetDecimal(out var minAmt))
                {
                    tiers.Add((minAmt, rate));
                }
            }
        }
        Console.WriteLine("Parsed count: " + tiers.Count);
    }
}
