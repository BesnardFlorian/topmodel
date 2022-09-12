﻿using TopModel.Core;
using TopModel.Utils;

namespace TopModel.Generator.Jpa;

public static class JpaUtils
{
    public static string GetJavaType(this IProperty prop)
    {
        return prop switch
        {
            AssociationProperty a => a.GetJavaType(),
            CompositionProperty c => c.GetJavaType(),
            AliasProperty l => l.GetJavaType(),
            RegularProperty r => r.GetJavaType(),
            _ => string.Empty,
        };
    }

    public static string GetJavaName(this IProperty prop)
    {
        string propertyName = prop.Name.ToFirstLower();
        if (prop is AssociationProperty ap)
        {
            propertyName = ap.GetAssociationName();
        }

        return propertyName;
    }

    public static string GetJavaType(this AssociationProperty ap)
    {
        var isList = ap.Type == AssociationType.OneToMany || ap.Type == AssociationType.ManyToMany;
        if (isList)
        {
            return $"List<{ap.Association.Name}>";
        }

        return ap.Association.Name;
    }

    public static string GetAssociationName(this AssociationProperty ap)
    {
        if (ap.Type == AssociationType.ManyToMany || ap.Type == AssociationType.OneToMany)
        {
            return $"{ap.Name.ToFirstLower()}";
        }
        else
        {
            return $"{ap.Association.Name.ToFirstLower()}{ap.Role?.ToFirstUpper() ?? string.Empty}";
        }
    }

    public static string GetJavaType(this AliasProperty ap)
    {
        if (ap.Class != null && ap.Class.IsPersistent)
        {
            if (ap.Property is AssociationProperty asp)
            {
                if (asp.IsEnum())
                {
                    return asp.Property.GetJavaType();
                }
                else
                {
                    return ap.Property.Domain.Java!.Type;
                }
            }
            else
            {
                return ap.Property.GetJavaType();
            }
        }

        if (ap.IsEnum())
        {
            return ap.Property.GetJavaType();
        }
        else if (ap.Property is AssociationProperty apr)
        {
            if (apr.Type == AssociationType.ManyToMany || apr.Type == AssociationType.OneToMany)
            {
                return $"List<{apr.Property.GetJavaType()}>";
            }

            return apr.Property.GetJavaType();
        }
        else if (ap.Property is CompositionProperty cpo)
        {
            if (cpo.Kind == "list")
            {
                return $"List<{cpo.Composition.Name}>";
            }
            else if (cpo.Kind == "object")
            {
                return cpo.Composition.Name;
            }
            else if (cpo.DomainKind != null)
            {
                if (cpo.DomainKind.Java!.Type.Contains("{class}"))
                {
                    return cpo.DomainKind.Java.Type.Replace("{class}", cpo.Composition.Name);
                }

                return $"{cpo.DomainKind.Java.Type}<{cpo.Composition.Name}>";
            }
        }

        return ap.Domain.Java!.Type;
    }

    public static string GetJavaType(this RegularProperty rp)
    {
        return rp.IsEnum() ? $"{rp.Class.Name.ToFirstUpper()}.Values" : rp.Domain.Java!.Type;
    }

    public static string GetJavaType(this CompositionProperty cp)
    {
        return cp.Kind switch
        {
            "object" => cp.Composition.Name,
            "list" => $"List<{cp.Composition.Name}>",
            "async-list" => $"IAsyncEnumerable<{cp.Composition.Name}>",
            string _ when cp.DomainKind!.Java!.Type.Contains("{class}") => cp.DomainKind.Java.Type.Replace("{class}", cp.Composition.Name),
            string _ => $"{cp.DomainKind.Java.Type}<{cp.Composition.Name}>"
        };
    }

    public static bool IsEnum(this IFieldProperty rp)
    {
        return rp.Class != null
                && rp.Class.IsPersistent
                && rp.Class.Reference
                && rp.Class.ReferenceValues.Count > 0
                && rp.PrimaryKey
                && rp.Domain.AutoGeneratedValue == false;
    }

    public static bool IsEnum(this AliasProperty ap)
    {
        return ap.Property is RegularProperty rp && rp.IsEnum();
    }

    public static bool IsAssociatedEnum(this AliasProperty ap)
    {
        return ap.Property is AssociationProperty apr && apr.IsEnum();
    }

    public static bool IsEnum(this AssociationProperty apr)
    {
        return apr.Property != null && apr.Property.IsEnum();
    }

    public static List<AssociationProperty> GetReverseProperties(this Class classe, List<Class> availableClasses)
    {
        if (classe.Reference)
        {
            return new List<AssociationProperty>();
        }

        return availableClasses
                    .SelectMany(c => c.Properties)
                    .OfType<AssociationProperty>()
                    .Where(p => !(p is JpaAssociationProperty))
                    .Where(p => p.Type != AssociationType.OneToOne)
                    .Where(p => p.Association == classe
                                && p.Class.Namespace.Module.Split('.').First() == classe.Namespace.Module.Split('.').First())
                    .ToList();
    }

    public static IList<IProperty> GetProperties(this Class classe, JpaConfig config,  List<Class> availableClasses)
    {
        if (classe.Reference)
        {
            return classe.Properties;
        }

        return classe.Properties.Concat(classe.GetReverseProperties(availableClasses).Select(p => new JpaAssociationProperty()
        {
            Association = p.Class,
            Type = p.Type == AssociationType.OneToMany ? AssociationType.ManyToOne
                                : p.Type == AssociationType.ManyToOne ? AssociationType.OneToMany
                                : AssociationType.ManyToMany,
            Comment = $"Association réciproque de {{@link {p.Class.GetPackageName(config)}.{p.Class}#{p.GetJavaName()} {p.Class.Name}.{p.GetJavaName()}}}",
            Class = classe,
            ReverseProperty = p,
            Role = p.Role
        })).ToList();
    }

    public static string GetPackageName(this Class classe, JpaConfig config)
    {
        var packageRoot = classe.IsPersistent ? config.EntitiesPackageName : config.DtosPackageName;
        return $"{packageRoot}.{classe.Namespace.Module.ToLower()}";
    }
}
