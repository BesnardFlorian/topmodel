﻿using TopModel.Core.FileModel;

namespace TopModel.Core;

public class AssociationProperty : IFieldProperty
{
#nullable disable
    public Class Association { get; set; }
#nullable enable

    public string? Label { get; set; }

#nullable disable
    public string Comment { get; set; }

    public Class Class { get; set; }

    public Endpoint Endpoint { get; set; }
#nullable enable

    public string? Role { get; set; }

    public AssociationType Type { get; set; }

    public bool Required { get; set; }

    public string? DefaultValue { get; set; }

    public LocatedString Name => new LocatedString(Association?.Properties.Single(p => p.PrimaryKey).Name)
    {
        Value = (Association?.Extends == null && !AsAlias ? Association?.Name : string.Empty) + Association?.Properties.Single(p => p.PrimaryKey).Name + (Role?.Replace(" ", string.Empty) ?? string.Empty),
        Location = Association?.Properties.Single(p => p.PrimaryKey).Name.Location
    };

    public Domain Domain => Association.Properties.OfType<IFieldProperty>().Single(p => p.PrimaryKey).Domain;

    public bool PrimaryKey => false;

    public bool AsAlias { get; set; }

#nullable disable
    internal Reference Location { get; set; }

    public ClassReference Reference { get; set; }
}