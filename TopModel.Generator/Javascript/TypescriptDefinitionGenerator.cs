﻿using Microsoft.Extensions.Logging;
using TopModel.Core;
using TopModel.Core.FileModel;
using TopModel.Utils;

namespace TopModel.Generator.Javascript;

/// <summary>
/// Générateur de définitions Typescript.
/// </summary>
public class TypescriptDefinitionGenerator : GeneratorBase
{
    private readonly JavascriptConfig _config;
    private readonly ILogger<TypescriptDefinitionGenerator> _logger;

    public TypescriptDefinitionGenerator(ILogger<TypescriptDefinitionGenerator> logger, JavascriptConfig config)
        : base(logger, config)
    {
        _config = config;
        _logger = logger;
    }

    public override string Name => "JSDefinitionGen";

    public override IEnumerable<string> GeneratedFiles =>
        Files.SelectMany(f => GetClasses(f.Value)).SelectMany(c => _config.Tags.Intersect(c.ModelFile.Tags).Select(tag => _config.GetClassFileName(c, tag)))
            .Concat(Files.SelectMany(f => f.Value.Classes).SelectMany(c => _config.Tags.Intersect(c.ModelFile.Tags).Select(tag => _config.GetReferencesFileName(c.ModelFile.Module, tag))))
            .Distinct();

    protected override void HandleFiles(IEnumerable<ModelFile> files)
    {
        foreach (var file in files)
        {
            GenerateClasses(file);
        }

        var modules = files.SelectMany(f => f.Classes.Select(c => c.Namespace.Module)).Distinct();

        foreach (var module in modules)
        {
            GenerateReferences(module);
        }
    }

    private List<Class> GetClasses(ModelFile file)
    {
        return file.Classes.Where(classe => !(classe.Reference || classe.ReferenceValues.Any()) || classe.PrimaryKey?.Domain.AutoGeneratedValue == true).ToList();
    }

    private void GenerateClasses(ModelFile file)
    {
        foreach (var classe in GetClasses(file))
        {
            foreach (var (tag, fileName) in _config.Tags.Intersect(file.Tags)
                 .Select(tag => (tag, fileName: _config.GetClassFileName(classe, tag)))
                 .DistinctBy(t => t.fileName))
            {
                GenerateClassFile(fileName, classe, tag);
            }
        }
    }

    private void GenerateReferences(string module)
    {
        foreach (var group in _config.Tags
            .Select(tag => (tag, fileName: _config.GetReferencesFileName(module, tag)))
            .GroupBy(t => t.fileName))
        {
            var classes = Classes
                .Where(c => c.ModelFile.Tags.Intersect(group.Select(t => t.tag)).Any()
                    && c.Namespace.Module == module
                    && (c.Reference || c.ReferenceValues.Any())
                    && c.PrimaryKey?.Domain.AutoGeneratedValue != true);

            if (classes.Any())
            {
                GenerateReferenceFile(group.Key, classes.OrderBy(r => r.Name), group.First().tag);
            }
        }
    }

    private IEnumerable<string> GetFocusStoresImports(Class classe)
    {
        if (classe.Properties.Any(p => p is IFieldProperty || p is CompositionProperty cp && cp.DomainKind != null))
        {
            yield return "FieldEntry2";
        }

        if (classe.Properties.Any(p => p is CompositionProperty { Kind: "list" } cp && cp.Class == classe))
        {
            yield return "ListEntry";
        }

        if (classe.Properties.Any(p => p is CompositionProperty { Kind: "object" }))
        {
            yield return "ObjectEntry";
        }

        if (classe.Properties.Any(p => p is CompositionProperty { Kind: "list" } cp && cp.Composition == classe))
        {
            yield return "RecursiveListEntry";
        }

        foreach (var p in classe.Properties.OfType<CompositionProperty>().Where(p => p.DomainKind?.TS?.Import == "@focus4/stores"))
        {
            yield return p.DomainKind!.TS!.Type.ParseTemplate(p).Split('<').First();
        }

        yield return "EntityToType";
        yield return "StoreNode";
    }

    private void GenerateClassFile(string fileName, Class classe, string tag)
    {
        using var fw = new FileWriter(fileName, _logger, false);

        if (_config.TargetFramework == TargetFramework.FOCUS)
        {
            fw.WriteLine($"import {{{string.Join(", ", GetFocusStoresImports(classe).OrderBy(x => x))}}} from \"@focus4/stores\";");
        }

        var domains = classe.DomainDependencies
            /* Cette vérification est nécessaire car pour un alias avec ListDomain les deux domaines sont dans les dépendances...*/
            .Where(d => classe.Properties.Any(p => d.Domain == ((p as AliasProperty)?.ListDomain ?? (p as CompositionProperty)?.DomainKind ?? (p as IFieldProperty)?.Domain)))
            .OrderBy(d => d.Domain.Name)
            .ToList();

        if (domains.Any())
        {
            var domainImport = _config.DomainImportPath.StartsWith("@")
                ? _config.DomainImportPath
                : Path.GetRelativePath(string.Join('/', fileName.Split('/').SkipLast(1)), Path.Combine(_config.OutputDirectory, _config.ModelRootPath!.Replace("{tag}", tag.ToKebabCase()), _config.DomainImportPath)).Replace("\\", "/");
            fw.WriteLine($"import {{{string.Join(", ", domains.Select(d => d.Domain.Name).Distinct())}}} from \"{domainImport}\";");
        }

        var imports = classe.ClassDependencies
            .Select(dep => (
                Import: dep is { Source: CompositionProperty { DomainKind: not null } }
                    ? dep.Classe.Name
                    : dep is { Source: IFieldProperty fp }
                    ? fp.GetPropertyTypeName(Classes).Replace("[]", string.Empty)
                    : $"{dep.Classe.Name}Entity, {dep.Classe.Name}{(_config.TargetFramework == TargetFramework.FOCUS ? "EntityType" : string.Empty)}",
                Path: _config.GetImportPathForClass(dep, tag)!))
            .Concat(classe.DomainDependencies.Select(p => (Import: p.Domain.TS!.Type.ParseTemplate(p.Source).Split("<").First(), Path: p.Domain.TS.Import!.ParseTemplate(p.Source))))
            .Where(p => p.Path != null && p.Path != "@focus4/stores")
            .GroupAndSort();

        fw.WriteLine();

        foreach (var import in imports)
        {
            fw.WriteLine($"import {{{import.Import}}} from \"{import.Path}\";");
        }

        if (imports.Any())
        {
            fw.WriteLine();
        }

        if (_config.TargetFramework == TargetFramework.FOCUS)
        {
            fw.Write("export type ");
            fw.Write(classe.Name);
            fw.Write(" = EntityToType<");
            fw.Write(classe.Name);
            fw.Write("EntityType>;\r\nexport type ");
            fw.Write(classe.Name);
            fw.Write("Node = StoreNode<");
            fw.Write(classe.Name);
            fw.Write("EntityType>;\r\n");

            fw.Write($"export interface {classe.Name}EntityType ");

            if (classe.Extends != null)
            {
                fw.Write($"extends {classe.Extends.Name}EntityType ");
            }
        }
        else
        {
            fw.Write("export interface ");
            fw.Write($"{classe.Name} ");

            if (classe.Extends != null)
            {
                fw.Write($"extends {classe.Extends.Name} ");
            }
        }

        fw.Write("{\r\n");

        foreach (var property in classe.Properties)
        {
            fw.Write($"    {property.Name.ToFirstLower()}{(_config.TargetFramework == TargetFramework.FOCUS ? string.Empty : "?")}: ");

            if (_config.TargetFramework == TargetFramework.FOCUS)
            {
                if (property is CompositionProperty cp)
                {
                    if (cp.Kind == "list")
                    {
                        if (cp.Composition.Name == classe.Name)
                        {
                            fw.Write($"RecursiveListEntry");
                        }
                        else
                        {
                            fw.Write($"ListEntry<{cp.Composition.Name}EntityType>");
                        }
                    }
                    else if (cp.Kind == "object")
                    {
                        fw.Write($"ObjectEntry<{cp.Composition.Name}EntityType>");
                    }
                    else
                    {
                        fw.Write($"FieldEntry2<typeof {cp.Kind}, {cp.GetPropertyTypeName(Classes)}>");
                    }
                }
                else if (property is IFieldProperty field)
                {
                    var domain = (field as AliasProperty)?.ListDomain ?? field.Domain;
                    fw.Write($"FieldEntry2<typeof {domain.Name}, {field.GetPropertyTypeName(Classes)}>");
                }
            }
            else
            {
                fw.Write(property.GetPropertyTypeName(Classes));
            }

            if (property != classe.Properties.Last())
            {
                fw.Write(",");
            }

            fw.Write("\r\n");
        }

        fw.Write("}\r\n\r\n");

        fw.Write($"export const {classe.Name}Entity");

        if (_config.TargetFramework == TargetFramework.FOCUS)
        {
            fw.Write($": {classe.Name}EntityType");
        }

        fw.Write(" = {\r\n");

        if (classe.Extends != null)
        {
            fw.Write("    ...");
            fw.Write(classe.Extends.Name);
            fw.Write("Entity,\r\n");
        }

        foreach (var property in classe.Properties)
        {
            fw.Write("    ");
            fw.Write(property.Name.ToFirstLower());
            fw.Write(": {\r\n");
            fw.Write("        type: ");

            if (property is CompositionProperty cp)
            {
                if (cp.Kind == "list")
                {
                    if (cp.Composition.Name == classe.Name)
                    {
                        fw.Write("\"recursive-list\"");
                    }
                    else
                    {
                        fw.Write("\"list\",");
                    }
                }
                else if (cp.Kind == "object")
                {
                    fw.Write("\"object\",");
                }
                else
                {
                    fw.Write("\"field\",");
                }
            }
            else
            {
                fw.Write("\"field\",");
            }

            fw.Write("\r\n");

            if (property is IFieldProperty field)
            {
                fw.Write("        name: \"");
                fw.Write(field.Name.ToFirstLower());
                fw.Write("\"");
                fw.Write(",\r\n        domain: ");
                var domain = (field as AliasProperty)?.ListDomain ?? field.Domain;
                fw.Write(domain.Name);
                fw.Write(",\r\n        isRequired: ");
                fw.Write((field.Required && !field.PrimaryKey).ToString().ToFirstLower());
                fw.Write(",\r\n        label: \"");
                fw.Write(field.ResourceKey);
                fw.Write("\"\r\n");
            }
            else if (property is CompositionProperty cp3 && cp3.DomainKind != null)
            {
                fw.Write("        name: \"");
                fw.Write(cp3.Name.ToFirstLower());
                fw.Write("\"");
                fw.Write(",\r\n        domain: ");
                fw.Write(cp3.DomainKind.Name);
                fw.Write(",\r\n        isRequired: true");
                fw.Write(",\r\n        label: \"");
                fw.Write(classe.Namespace.Module.ToFirstLower());
                fw.Write(".");
                fw.Write(classe.Name.ToFirstLower());
                fw.Write(".");
                fw.Write(property.Name.ToFirstLower());
                fw.Write("\"\r\n");
            }
            else if (property is CompositionProperty cp2 && cp2.Composition.Name != classe.Name)
            {
                fw.Write("        entity: ");
                fw.Write(cp2.Composition.Name);
                fw.Write("Entity");
                fw.Write("\r\n");
            }

            fw.Write("    }");

            if (property != classe.Properties.Last())
            {
                fw.Write(",");
            }

            fw.Write("\r\n");
        }

        fw.Write($"}}{(_config.TargetFramework == TargetFramework.FOCUS ? string.Empty : " as const")}\r\n");

        if (classe.Reference)
        {
            fw.WriteLine();
            WriteReferenceDefinition(fw, classe);
        }
    }

    /// <summary>
    /// Create the template output
    /// </summary>
    private void GenerateReferenceFile(string fileName, IEnumerable<Class> references, string tag)
    {
        using var fw = new FileWriter(fileName, _logger, false);

        var module = references.First().Namespace.Module;
        var imports = references
            .SelectMany(r => r.ClassDependencies)
            .Select(dep => (
                Import: dep.Source switch
                {
                    IProperty fp => fp.GetPropertyTypeName(Classes),
                    Class c => c.Name,
                    _ => null!
                },
                Path: _config.GetImportPathForClass(dep, tag)!))
            .Concat(references.SelectMany(r => r.DomainDependencies).Select(p => (Import: p.Domain.TS!.Type.ParseTemplate(p.Source).Split("<").First(), Path: p.Domain.TS.Import!.ParseTemplate(p.Source))))
            .Where(i => i.Path != null && i.Path != $"./references")
            .GroupAndSort();

        foreach (var import in imports)
        {
            fw.Write("import {");
            fw.Write(import.Import);
            fw.Write("} from \"");
            fw.Write(import.Path);
            fw.Write("\";\r\n");
        }

        if (imports.Any())
        {
            fw.Write("\r\n");
        }

        var first = true;
        foreach (var reference in references)
        {
            if (first)
            {
                first = false;
            }
            else
            {
                fw.WriteLine();
            }

            var valueProperty = reference.PrimaryKey ?? reference.Properties.OfType<IFieldProperty>().First();

            fw.Write("export type ");
            fw.Write(reference.Name);
            fw.Write($"{valueProperty.Name} = ");
            fw.Write(reference.ReferenceValues.Any()
                ? string.Join(" | ", reference.ReferenceValues.Select(r => $@"""{r.Value[valueProperty]}""").OrderBy(x => x, StringComparer.Ordinal))
                : valueProperty.Domain.TS?.Type.ParseTemplate(valueProperty));
            fw.WriteLine(";");

            if (reference.FlagProperty != null && reference.ReferenceValues.Any())
            {
                fw.Write($"export enum {reference.Name}Flag {{\r\n");

                var flagValues = reference.ReferenceValues.Where(refValue => refValue.Value.ContainsKey(reference.FlagProperty) && int.TryParse(refValue.Value[reference.FlagProperty], out var _)).ToList();
                foreach (var refValue in flagValues)
                {
                    var flag = int.Parse(refValue.Value[reference.FlagProperty]);
                    fw.Write($"    {refValue.Name} = 0b{Convert.ToString(flag, 2)}");
                    if (flagValues.IndexOf(refValue) != flagValues.Count - 1)
                    {
                        fw.WriteLine(",");
                    }
                }

                fw.WriteLine("\r\n}");
            }

            fw.Write("export interface ");
            fw.Write(reference.Name);
            fw.Write(" {\r\n");

            foreach (var property in reference.Properties.OfType<IFieldProperty>())
            {
                fw.Write("    ");
                fw.Write(property.Name.ToFirstLower());
                fw.Write(property.Required || property.PrimaryKey ? string.Empty : "?");
                fw.Write(": ");
                fw.Write(property.GetPropertyTypeName(Classes));
                fw.Write(";\r\n");
            }

            fw.Write("}\r\n");
            if (_config.ReferenceMode == ReferenceMode.VALUES)
            {
                WriteReferenceValues(fw, reference);
            }
            else
            {
                WriteReferenceDefinition(fw, reference);
            }
        }
    }

    private void WriteReferenceValues(FileWriter fw, Class reference)
    {
        fw.Write("export const ");
        fw.Write(reference.Name.ToFirstLower());
        fw.Write($"List: {reference.Name}[] = [");
        fw.WriteLine();
        foreach (var refValue in reference.ReferenceValues)
        {
            fw.WriteLine("    {");
            fw.Write("        ");
            fw.Write(string.Join(",\n        ", refValue.Value.Where(p => p.Value != "null").Select(property => $"{property.Key.Name.ToFirstLower()}: {(property.Key.Domain.TS!.Type == "string" ? @$"""{property.Value}""" : @$"{(property.Value)}")}")));
            fw.WriteLine();
            fw.WriteLine("    },");
        }

        fw.WriteLine("];");
        fw.WriteLine();
    }

    private void WriteReferenceDefinition(FileWriter fw, Class classe)
    {
        fw.Write("export const ");
        fw.Write(classe.Name.ToFirstLower());
        fw.Write(" = {type: {} as ");
        fw.Write(classe.Name);
        fw.Write(", valueKey: \"");
        fw.Write((classe.PrimaryKey ?? classe.Properties.OfType<IFieldProperty>().First()).Name.ToFirstLower());
        fw.Write("\", labelKey: \"");
        fw.Write(classe.DefaultProperty?.Name.ToFirstLower());
        fw.Write("\"} as const;\r\n");
    }
}