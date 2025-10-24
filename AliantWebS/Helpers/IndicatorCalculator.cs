using AliantWebS.Data;
using AliantWebS.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace AliantWebS.IndicatorCalculator
{
    public class IndicatorCalculator
    {
        private readonly EntitiesService _entitiesService;

        public IndicatorCalculator(EntitiesService entitiesService)
        {
            _entitiesService = entitiesService;
        }

        public async Task<PreviewResult> CalculateIndicatorAsync(CustomIndicator indicator, Structure structure)
        {
            try
            {
                if (!indicator.Criteria.Any())
                {
                    return new PreviewResult
                    {
                        Value = 0,
                        ErrorMessage = "Aucun critère défini pour le calcul."
                    };
                }

                var result = new PreviewResult
                {
                    CalculatedAt = DateTime.Now,
                    Details = new List<PreviewDetail>()
                };

                var criteriaValues = new Dictionary<string, double>();

                // Calculer chaque critère
                for (int i = 0; i < indicator.Criteria.Count; i++)
                {
                    var criteria = indicator.Criteria[i];
                    try
                    {
                        var criteriaValue = await CalculateCriteriaValueAsync(criteria, structure);
                        var variableName = $"C{i + 1}";
                        criteriaValues[variableName] = criteriaValue;

                        result.Details.Add(new PreviewDetail
                        {
                            CriteriaName = criteria.Name,
                            Value = criteriaValue,
                            Weight = criteria.Weight
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erreur lors du calcul du critère {criteria.Name}: {ex.Message}");
                        result.Details.Add(new PreviewDetail
                        {
                            CriteriaName = criteria.Name,
                            Value = 0,
                            Weight = criteria.Weight,
                            Error = ex.Message
                        });
                    }
                }

                // Calculer la valeur finale
                if (indicator.UseCustomFormula && !string.IsNullOrEmpty(indicator.CustomFormula))
                {
                    result.Value = EvaluateCustomFormula(indicator.CustomFormula, criteriaValues, result.Details);
                }
                else
                {
                    // Calcul par moyenne pondérée
                    var totalWeightedValue = result.Details.Sum(d => d.Value * d.Weight);
                    var totalWeight = result.Details.Sum(d => d.Weight);
                    result.Value = totalWeight > 0 ? totalWeightedValue / totalWeight : 0;
                }

                return result;
            }
            catch (Exception ex)
            {
                return new PreviewResult
                {
                    Value = 0,
                    ErrorMessage = $"Erreur lors du calcul de l'indicateur: {ex.Message}",
                    CalculatedAt = DateTime.Now
                };
            }
        }

        private async Task<double> CalculateCriteriaValueAsync(IndicatorCriteria criteria, Structure structure)
        {
            return criteria.DataSource switch
            {
                "structure_attributes" => await CalculateStructureAttributeCriteriaAsync(criteria, structure),
                "structure_inspections" => await CalculateStructureInspectionCriteriaAsync(criteria, structure),
                "element_attributes" => await CalculateElementAttributeCriteriaAsync(criteria, structure),
                "element_inspections" => await CalculateElementInspectionCriteriaAsync(criteria, structure),
                "defect_attributes" => await CalculateDefectAttributeCriteriaAsync(criteria, structure),
                _ => 0
            };
        }

        private async Task<double> CalculateStructureAttributeCriteriaAsync(IndicatorCriteria criteria, Structure structure)
        {
            if (!criteria.AttributeTypeId.HasValue || structure == null)
                return 0;

            var attributes = await _entitiesService.GetFilteredEntitiesAsync<AttributeStructure>(
                x => x.StructureId == structure.StructureId &&
                     x.AttributeTypeId == criteria.AttributeTypeId.Value);

            if (!attributes.Any()) return 0;

            var numericValues = new List<double>();
            foreach (var attr in attributes)
            {
                if (double.TryParse(attr.Value, out double value))
                {
                    numericValues.Add(value);
                }
            }

            if (!numericValues.Any()) return attributes.Count;

            return criteria.CalculationMethod switch
            {
                "average" => numericValues.Average(),
                "max" => numericValues.Max(),
                "min" => numericValues.Min(),
                "sum" => numericValues.Sum(),
                "count" => numericValues.Count,
                "weighted_average" => numericValues.Average(), // Same as average for single criteria
                _ => numericValues.Average()
            };
        }

        private async Task<double> CalculateStructureInspectionCriteriaAsync(IndicatorCriteria criteria, Structure structure)
        {
            if (!criteria.InspectionResultTypeId.HasValue || structure == null)
                return 0;

            var results = await _entitiesService.GetFilteredEntitiesAsync<StructureInspectionResult>(
                x => x.StructureId == structure.StructureId &&
                     x.InspectionResultTypeId == criteria.InspectionResultTypeId.Value &&
                     x.ResultNumeric.HasValue);

            if (!results.Any()) return 0;

            var values = results.Select(r => (double)r.ResultNumeric.Value).ToList();

            return criteria.CalculationMethod switch
            {
                "average" => values.Average(),
                "max" => values.Max(),
                "min" => values.Min(),
                "sum" => values.Sum(),
                "count" => values.Count,
                "critical_percentage" => values.Count(v => v > 7.0) / (double)values.Count * 100,
                "weighted_average" => values.Average(), // Same as average for single criteria
                _ => values.Average()
            };
        }

        private async Task<double> CalculateElementAttributeCriteriaAsync(IndicatorCriteria criteria, Structure structure)
        {
            if (!criteria.ElementTypeId.HasValue || !criteria.AttributeTypeId.HasValue || structure == null)
                return 0;

            var elements = await _entitiesService.GetFilteredEntitiesAsync<Element>(
                x => x.StructureId == structure.StructureId &&
                     x.ElementTypeId == criteria.ElementTypeId.Value);

            var elementIds = elements.Select(e => e.ElementId).ToList();

            var attributes = await _entitiesService.GetFilteredEntitiesAsync<AttributeElement>(
                x => elementIds.Contains(x.ElementId) &&
                     x.AttributeTypeId == criteria.AttributeTypeId.Value);

            if (!attributes.Any()) return 0;

            var numericValues = new List<double>();
            foreach (var attr in attributes)
            {
                if (double.TryParse(attr.Value, out double value))
                {
                    numericValues.Add(value);
                }
            }

            if (!numericValues.Any()) return attributes.Count;

            return criteria.CalculationMethod switch
            {
                "average" => numericValues.Average(),
                "max" => numericValues.Max(),
                "min" => numericValues.Min(),
                "sum" => numericValues.Sum(),
                "count" => numericValues.Count,
                "critical_percentage" => numericValues.Count(v => v > 3.0) / (double)numericValues.Count * 100,
                "weighted_average" => numericValues.Average(), // Same as average for single criteria
                _ => numericValues.Average()
            };
        }

        private async Task<double> CalculateElementInspectionCriteriaAsync(IndicatorCriteria criteria, Structure structure)
        {
            if (!criteria.ElementTypeId.HasValue || !criteria.InspectionResultTypeId.HasValue || structure == null)
                return 0;

            var elements = await _entitiesService.GetFilteredEntitiesAsync<Element>(
                x => x.StructureId == structure.StructureId &&
                     x.ElementTypeId == criteria.ElementTypeId.Value);

            var elementIds = elements.Select(e => e.ElementId).ToList();

            var results = await _entitiesService.GetFilteredEntitiesAsync<InspectionResult>(
                x => elementIds.Contains(x.ElementId) &&
                     x.InspectionResultTypeId == criteria.InspectionResultTypeId.Value &&
                     x.ResultNumeric.HasValue);

            if (!results.Any()) return 0;

            var values = results.Select(r => (double)r.ResultNumeric.Value).ToList();

            return criteria.CalculationMethod switch
            {
                "average" => values.Average(),
                "max" => values.Max(),
                "min" => values.Min(),
                "sum" => values.Sum(),
                "count" => values.Count,
                "critical_percentage" => values.Count(v => v > 7.0) / (double)values.Count * 100,
                "weighted_average" => values.Average(), // Same as average for single criteria
                _ => values.Average()
            };
        }

        private async Task<double> CalculateDefectAttributeCriteriaAsync(IndicatorCriteria criteria, Structure structure)
        {
            if (!criteria.ElementTypeId.HasValue || !criteria.AttributeTypeId.HasValue || structure == null)
                return 0;

            var elements = await _entitiesService.GetFilteredEntitiesAsync<Element>(
                x => x.StructureId == structure.StructureId &&
                     x.ElementTypeId == criteria.ElementTypeId.Value);

            var elementIds = elements.Select(e => e.ElementId).ToList();

            var defects = await _entitiesService.GetFilteredEntitiesAsync<Defect>(
                x => elementIds.Contains(x.ElementId));

            if (criteria.DefectTypeId.HasValue)
            {
                defects = defects.Where(d => d.DefectTypeId == criteria.DefectTypeId.Value).ToList();
            }

            var defectIds = defects.Select(d => d.DefectId).ToList();

            var attributes = await _entitiesService.GetFilteredEntitiesAsync<AttributeDefect>(
                x => defectIds.Contains(x.DefectId) &&
                     x.AttributeTypeId == criteria.AttributeTypeId.Value);

            if (!attributes.Any()) return 0;

            var numericValues = new List<double>();
            foreach (var attr in attributes)
            {
                if (double.TryParse(attr.Value, out double value))
                {
                    numericValues.Add(value);
                }
            }

            if (!numericValues.Any()) return attributes.Count;

            return criteria.CalculationMethod switch
            {
                "average" => numericValues.Average(),
                "max" => numericValues.Max(),
                "min" => numericValues.Min(),
                "sum" => numericValues.Sum(),
                "count" => numericValues.Count,
                "critical_percentage" => numericValues.Count(v => v > 3.0) / (double)numericValues.Count * 100,
                "weighted_average" => numericValues.Average(), // Same as average for single criteria
                _ => numericValues.Average()
            };
        }

        private double EvaluateCustomFormula(string formula, Dictionary<string, double> variables, List<PreviewDetail> details)
        {
            try
            {
                var evaluationFormula = formula;
                foreach (var variable in variables)
                {
                    evaluationFormula = evaluationFormula.Replace(variable.Key, variable.Value.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
                }

                // Handle basic math functions
                evaluationFormula = ProcessMathFunctions(evaluationFormula);

                var table = new DataTable();
                var result = table.Compute(evaluationFormula, null);
                return Convert.ToDouble(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error evaluating custom formula: {ex.Message}");
                // Fallback to weighted average
                var totalWeight = details.Sum(d => d.Weight);
                return totalWeight > 0 ? details.Sum(d => d.Value * d.Weight) / totalWeight : 0;
            }
        }

        private string ProcessMathFunctions(string formula)
        {
            // Simple preprocessing for basic math functions
            // Note: This is a basic implementation. For more complex formulas, 
            // consider using a proper expression evaluator library
            
            // Handle MAX function (simplified - only for two values)
            formula = System.Text.RegularExpressions.Regex.Replace(formula, 
                @"MAX\(([^,]+),([^)]+)\)", 
                m => $"IIF({m.Groups[1].Value} > {m.Groups[2].Value}, {m.Groups[1].Value}, {m.Groups[2].Value})");
            
            // Handle MIN function (simplified - only for two values)
            formula = System.Text.RegularExpressions.Regex.Replace(formula, 
                @"MIN\(([^,]+),([^)]+)\)", 
                m => $"IIF({m.Groups[1].Value} < {m.Groups[2].Value}, {m.Groups[1].Value}, {m.Groups[2].Value})");
            
            // Handle SQRT function
            formula = System.Text.RegularExpressions.Regex.Replace(formula, 
                @"SQRT\(([^)]+)\)", 
                m => $"POWER({m.Groups[1].Value}, 0.5)");
            
            // Handle ABS function
            formula = System.Text.RegularExpressions.Regex.Replace(formula, 
                @"ABS\(([^)]+)\)", 
                m => $"IIF({m.Groups[1].Value} < 0, -{m.Groups[1].Value}, {m.Groups[1].Value})");

            return formula;
        }

        public static string ValidateFormula(string formula, List<IndicatorCriteria> criteria)
        {
            if (string.IsNullOrWhiteSpace(formula))
            {
                return "La formule ne peut pas être vide.";
            }

            try
            {
                var trimmedFormula = formula.Trim();

                // Vérifier que toutes les variables utilisées existent
                var availableVariables = criteria.Select((c, i) => $"C{i + 1}").ToList();
                var usedVariables = System.Text.RegularExpressions.Regex.Matches(trimmedFormula, @"C\d+")
                    .Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => m.Value)
                    .Distinct();

                var invalidVariables = usedVariables.Where(v => !availableVariables.Contains(v)).ToList();
                if (invalidVariables.Any())
                {
                    return $"Variables non définies : {string.Join(", ", invalidVariables)}";
                }

                // Vérifier les parenthèses équilibrées
                var openParens = trimmedFormula.Count(c => c == '(');
                var closeParens = trimmedFormula.Count(c => c == ')');
                if (openParens != closeParens)
                {
                    return "Parenthèses non équilibrées.";
                }

                // Vérifier les caractères autorisés
                var allowedChars = "CMAXINVGSQRTBSIF0123456789.,+-*/() ";
                if (trimmedFormula.Any(c => !allowedChars.Contains(char.ToUpper(c))))
                {
                    return "Caractères non autorisés dans la formule.";
                }

                return string.Empty; // Valid
            }
            catch (Exception ex)
            {
                return $"Erreur de validation : {ex.Message}";
            }
        }

        public static string GetFormulaPreview(List<IndicatorCriteria> criteria)
        {
            if (!criteria.Any()) return "";

            var parts = criteria.Select(c => $"({c.Name} × {c.Weight})").ToList();
            var totalWeight = criteria.Sum(c => c.Weight);
            return $"({string.Join(" + ", parts)}) / {totalWeight}";
        }

        public static string GetDefaultCustomFormula(List<IndicatorCriteria> criteria)
        {
            if (!criteria.Any()) return "";

            var variables = criteria.Select((c, i) => $"C{i + 1} * {c.Weight}").ToList();
            var totalWeight = criteria.Sum(c => c.Weight);
            return $"({string.Join(" + ", variables)}) / {totalWeight}";
        }
    }

    // Supporting classes that should be moved to a shared Models folder
    public class PreviewResult
    {
        public double Value { get; set; }
        public DateTime CalculatedAt { get; set; }
        public string ErrorMessage { get; set; } = "";
        public List<PreviewDetail> Details { get; set; } = new();
    }

    public class PreviewDetail
    {
        public string CriteriaName { get; set; } = "";
        public double Value { get; set; }
        public double Weight { get; set; }
        public string Error { get; set; } = "";
    }

    public class IndicatorCriteria
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string DataSource { get; set; } = "";
        public string CalculationMethod { get; set; } = "";
        public double Weight { get; set; } = 1.0;
        public Guid? InspectionResultTypeId { get; set; }
        public Guid? ElementTypeId { get; set; }
        public Guid? DefectTypeId { get; set; }
        public Guid? AttributeTypeId { get; set; }

        public IndicatorCriteria Clone()
        {
            return new IndicatorCriteria
            {
                Id = this.Id,
                Name = this.Name,
                Description = this.Description,
                DataSource = this.DataSource,
                CalculationMethod = this.CalculationMethod,
                Weight = this.Weight,
                InspectionResultTypeId = this.InspectionResultTypeId,
                ElementTypeId = this.ElementTypeId,
                DefectTypeId = this.DefectTypeId,
                AttributeTypeId = this.AttributeTypeId
            };
        }
    }
}