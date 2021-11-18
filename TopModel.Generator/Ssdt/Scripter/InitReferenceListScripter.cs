﻿using System.Text;
using TopModel.Core;

namespace TopModel.Generator.Ssdt.Scripter;

/// <summary>
/// Scripter permettant d'écrire les scripts d'initialisation des valeurs de listes de référence.
/// </summary>
public class InitReferenceListScripter : ISqlScripter<Class>
{
    /// <summary>
    /// Calcule le nom du script pour l'item.
    /// </summary>
    /// <param name="item">Item à scripter.</param>
    /// <returns>Nom du fichier de script.</returns>
    public string GetScriptName(Class item)
    {
        if (item == null)
        {
            throw new ArgumentNullException("item");
        }

        return item.SqlName + ".insert.sql";
    }

    /// <summary>
    /// Ecrit dans un flux le script pour l'item.
    /// </summary>
    /// <param name="writer">Flux d'écriture.</param>
    /// <param name="item">Table à scripter.</param>
    public void WriteItemScript(TextWriter writer, Class item)
    {
        if (writer == null)
        {
            throw new ArgumentNullException("writer");
        }

        if (item == null)
        {
            throw new ArgumentNullException("item");
        }

        var tableName = item.SqlName;

        // Entête du fichier.
        WriteHeader(writer, tableName);

        // Ecrit les inserts.
        WriteInsertLines(writer, item);

        WriteFooter(writer);
    }

    /// <summary>
    /// Retourne la ligne d'insert.
    /// </summary>
    /// <param name="modelClass">Modele de la classe.</param>
    /// <param name="initItem">Item a insérer.</param>
    /// <returns>Requête.</returns>
    private static string GetInsertLine(Class modelClass, ReferenceValue initItem)
    {
        // Remplissage d'un dictionnaire nom de colonne => valeur.
        var definition = initItem.Value;
        var nameValueDict = new Dictionary<string, string?>();
        foreach (var property in modelClass.Properties.OfType<IFieldProperty>())
        {
            if (!property.PrimaryKey || property.Domain.Name != "DO_ID")
            {
                nameValueDict[property.SqlName] = definition[property] switch
                {
                    null => "NULL",
                    bool b => b ? "N'true'" : "N'false'",
                    string bs when property.Domain.SqlType == "bit" => $"N'{bs}'",
                    string s when property.Domain.SqlType!.Contains("varchar") => $"N'{ScriptUtils.PrepareDataToSqlDisplay(s)}'",
                    object v => v.ToString()
                };
            }
        }

        // Création de la requête.
        var sb = new StringBuilder();
        sb.Append("INSERT INTO " + modelClass.SqlName + "(");
        var isFirst = true;
        foreach (var columnName in nameValueDict.Keys)
        {
            if (!isFirst)
            {
                sb.Append(", ");
            }

            isFirst = false;
            sb.Append(columnName);
        }

        sb.Append(") VALUES(");

        isFirst = true;
        foreach (var value in nameValueDict.Values)
        {
            if (!isFirst)
            {
                sb.Append(", ");
            }

            isFirst = false;
            sb.Append(value);
        }

        sb.Append(");");
        return sb.ToString();
    }

    /// <summary>
    /// Ecrit le pied du fichier.
    /// </summary>
    /// <param name="writer">Flux.</param>
    private void WriteFooter(TextWriter writer)
    {
        writer.WriteLine("GO");
    }

    /// <summary>
    /// Ecrit l'entête du fichier.
    /// </summary>
    /// <param name="writer">Flux.</param>
    /// <param name="tableName">Nom de la table.</param>
    private void WriteHeader(TextWriter writer, string tableName)
    {
        writer.WriteLine("-- ===========================================================================================");
        writer.WriteLine("--   Description		:	Insertion des valeurs de la table " + tableName + ".");
        writer.WriteLine("-- ===========================================================================================");
        writer.WriteLine();
    }

    /// <summary>
    /// Ecrit les lignes d'insertion pour la liste des valeurs.
    /// </summary>
    /// <param name="writer">Flux.</param>
    /// <param name="item">Liste de références.</param>
    private void WriteInsertLines(TextWriter writer, Class item)
    {
        foreach (var initItem in item.ReferenceValues!)
        {
            writer.WriteLine(GetInsertLine(item, initItem));
        }
    }
}