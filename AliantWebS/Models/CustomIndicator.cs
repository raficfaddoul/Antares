using AliantWebS.IndicatorCalculator;
using System;
using System.Collections.Generic;
using System.Linq;

public class CustomIndicator
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "";
    public double? AlertThreshold { get; set; }
    public string Unit { get; set; } = "";
    public List<IndicatorCriteria> Criteria { get; set; } = new();
    public DateTime CreatedDate { get; set; }
    public bool UseCustomFormula { get; set; } = false;
    public string CustomFormula { get; set; } = "";
    public string StorageMode { get; set; } = "database"; // New property to track storage mode
    public double? MaxValue { get; set; }

    public CustomIndicator Clone()
    {
        return new CustomIndicator
        {
            Id = this.Id,
            Name = this.Name,
            Description = this.Description,
            Type = this.Type,
            AlertThreshold = this.AlertThreshold,
            MaxValue = this.MaxValue,
            Unit = this.Unit,
            Criteria = this.Criteria.Select(c => c.Clone()).ToList(),
            CreatedDate = this.CreatedDate,
            UseCustomFormula = this.UseCustomFormula,
            CustomFormula = this.CustomFormula,
            StorageMode = this.StorageMode
        };
    }
}