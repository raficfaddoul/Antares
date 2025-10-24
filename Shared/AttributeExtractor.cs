using AliantWebS.Data;
using AliantWebS.Models;
using Microsoft.EntityFrameworkCore;

public static class AttributeExtractor
{
    /// <summary>
    /// Generic method to extract attributes from any entity following the standard attribute pattern
    /// </summary>
    /// <typeparam name="TEntity">The main entity (Structure, Element, Equipement, etc.)</typeparam>
    /// <typeparam name="TEntityType">The entity type (StructureType, ElementType, EquipementType, etc.)</typeparam>
    /// <typeparam name="TEntityTypeAttributeType">The junction table (StructureTypeAttributeType, etc.)</typeparam>
    /// <typeparam name="TAttributeEntity">The attribute entity (AttributeStructure, AttributeElement, etc.)</typeparam>
    public static async Task<List<(string Name, string Description, string Value)>> ExtractAttributesAsync<TEntity, TEntityType, TEntityTypeAttributeType, TAttributeEntity>(
        EntitiesService entitiesService,
        TEntity entity,
        string entityTypeIdPropertyName,
        string entityIdPropertyName,
        string attributeCollectionPropertyName)
        where TEntity : class
        where TEntityType : class
        where TEntityTypeAttributeType : class
        where TAttributeEntity : class
    {
        if (entity == null)
            return new List<(string Name, string Description, string Value)>();

        try
        {
            // Get EntityTypeId using reflection
            var entityTypeId = GetPropertyValue<Guid?>(entity, entityTypeIdPropertyName);
            if (entityTypeId == null)
                return new List<(string Name, string Description, string Value)>();

            // Load attribute collection if needed
            var attributeCollection = GetPropertyValue<ICollection<TAttributeEntity>>(entity, attributeCollectionPropertyName);
            if (attributeCollection?.Count == 0)
            {
                await entitiesService._appDBContext.Entry(entity)
                    .Collection(attributeCollectionPropertyName)
                    .LoadAsync();
            }

            // Get entity type with attribute types
            var entityTypeDbSet = entitiesService._appDBContext.Set<TEntityType>();
            var entityType = await entityTypeDbSet
                .Include($"{typeof(TEntityTypeAttributeType).Name}s.AttributeType")
                .FirstOrDefaultAsync(et => EF.Property<Guid>(et, entityTypeIdPropertyName) == entityTypeId);

            if (entityType == null)
                return new List<(string Name, string Description, string Value)>();

            // Get the junction collection property
            var junctionCollectionName = $"{typeof(TEntityTypeAttributeType).Name}s";
            var junctionCollection = GetPropertyValue<ICollection<TEntityTypeAttributeType>>(entityType, junctionCollectionName);

            var attributeList = new List<(string Name, string Description, string Value)>();

            foreach (var junction in junctionCollection ?? new List<TEntityTypeAttributeType>())
            {
                var attributeType = GetPropertyValue<AttributeType>(junction, "AttributeType");
                if (attributeType == null) continue;

                var attributeTypeId = GetPropertyValue<Guid>(junction, "AttributeTypeId");

                // Find corresponding attribute entity
                var attributeEntity = attributeCollection?.FirstOrDefault(x =>
                    GetPropertyValue<Guid?>(x, "AttributeTypeId") == attributeTypeId);

                if (attributeEntity != null)
                {
                    var value = GetPropertyValue<string>(attributeEntity, "Value");
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        attributeList.Add((
                            Name: attributeType.Name ?? string.Empty,
                            Description: attributeType.Description ?? string.Empty,
                            Value: value
                        ));
                    }
                }
            }

            return attributeList;
        }
        catch (Exception)
        {
            return new List<(string Name, string Description, string Value)>();
        }
    }

    // Helper method for reflection
    private static T GetPropertyValue<T>(object obj, string propertyName)
    {
        var property = obj.GetType().GetProperty(propertyName);
        if (property == null) return default(T);
        var value = property.GetValue(obj);
        return value == null ? default(T) : (T)value;
    }

    // Simplified methods for common entities
    public static async Task<List<(string Name, string Description, string Value)>> ExtractInfoAsync(
        EntitiesService entitiesService, 
        Structure structure)
    {
        return await ExtractAttributesAsync<Structure, StructureType, StructureTypeAttributeType, AttributeStructure>(
            entitiesService, structure, "StructureTypeId", "StructureId", "AttributeStructures");
    }

    public static async Task<List<(string Name, string Description, string Value)>> ExtractElementAttributesAsync(
        EntitiesService entitiesService, 
        Element element)
    {
        return await ExtractAttributesAsync<Element, ElementType, AttributeTypeElementType, AttributeElement>(
            entitiesService, element, "ElementTypeId", "ElementId", "AttributeElements");
    }

    public static async Task<List<(string Name, string Description, string Value)>> ExtractEquipementAttributesAsync(
        EntitiesService entitiesService, 
        Equipement equipement)
    {
        return await ExtractAttributesAsync<Equipement, EquipementType, EquipementTypeAttributeType, AttributeEquipement>(
            entitiesService, equipement, "EquipementTypeId", "EquipementId", "AttributeEquipements");
    }
}