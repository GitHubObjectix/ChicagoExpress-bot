using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

public enum RailwayCompanies
{
    Wabash = 0,
    PRR,
    BandO,
    NYC,
    CandO
}

public enum IndustrialCities
{
    Detroit = 0,
    Wheeling,
    Pittsburgh
}

public enum CostAndIncome
{
    ConstructionCost = 0,
    Income,
    IncomeIfDeveloped
}

public sealed class RailwayCompanyConfiguration
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Trains { get; set; }
    public int Stocks { get; set; }
    public int InitialBid { get; set; }
    public int InitialIncome { get; set; }
}

public static class ChicagoExpressSetup
{
    public static readonly RailwayCompanyConfiguration[] RailwayCompanies =
    {
        new RailwayCompanyConfiguration { Id = "Wabash", Name = "Wabash Railroad Company", Trains = 11, Stocks = 2, InitialBid = 0, InitialIncome = 1 },
        new RailwayCompanyConfiguration { Id = "PRR", Name = "The Pennsylvania Railroad Company", Trains = 20, Stocks = 3, InitialBid = 7, InitialIncome = 7 },
        new RailwayCompanyConfiguration { Id = "BandO", Name = "Baltimore & Ohio", Trains = 22, Stocks = 4, InitialBid = 6, InitialIncome = 6 },
        new RailwayCompanyConfiguration { Id = "NYC", Name = "New York Central", Trains = 24, Stocks = 5, InitialBid = 5, InitialIncome = 8 },
        new RailwayCompanyConfiguration { Id = "CandO", Name = "Chesapeake & Ohio", Trains = 26, Stocks = 6, InitialBid = 8, InitialIncome = 5 }
    };

    public const int Houses = 20;
    public const int IncomeTrackHorizontalLength = 30;
    public const int IncomeTrackVerticalLength = 20;
    public const int Cash = 1500;
    public const int Gauges = 3;

    public static readonly (int FirstRow, int LastRow)[] ColumnConfiguration =
    {
        (2, 4), (2, 6), (0, 7), (0, 7), (0, 8), (1, 8), (2, 9),
        (2, 9), (2, 10), (1, 9), (1, 10), (0, 9), (0, 10),
        (0, 9), (0, 10), (0, 9), (0, 9), (0, 6), (0, 5)
    };

    public static int FieldCount()
    {
        int numFields = 0;

        foreach(var column in ColumnConfiguration)
        {
            numFields += column.LastRow - column.FirstRow + 1;
        }

        return numFields;
    }

    public static int RowCount()
    {
        int rowCount = 0;

        foreach (var column in ColumnConfiguration)
        {
            rowCount = Math.Max(column.LastRow - column.FirstRow + 1, rowCount);
        }

        return rowCount;
    }

    public static int ColumnCount()
    {
        return ColumnConfiguration.Length;
    }

    // Each field stores construction cost, train income, and development income.
    public static readonly (int ConstructionCost, int Income, int IncomeIfDeveloped)[][] ColumnCostAndIncomeConfiguration =
    {
        new[] { (-3, 7, 0), (-1, 0, 0), (-1, 0, 0) },
        new[] { (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 2, 2), (-1, 0, 0) },
        new[] { (-1, 0, 0), (-1, 0, 0), (-2, 1, 2), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0) },
        new[] { (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 1, 2), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-2, 2, 2) },
        new[] { (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0) },
        new[] { (-2, 1, 1), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 1, 1), (-1, 0, 0), (-1, 0, 0), (-2, 2, 1) },
        new[] { (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-2, 0, 0), (-3, 1, 2) },
        new[] { (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-2, 0, 0), (-2, 0, 0), (-3, 1, 2) },
        new[] { (-1, 2, 2), (-1, 0, 0), (-2, 1, 1), (-2, 0, 0), (-3, 0, 0), (-2, 0, 0), (-4, 1, 2), (-3, 2, 1), (-3, 1, 2) },
        new[] { (-1, 0, 0), (-1, 0, 0), (-2, 0, 0), (-2, 0, 0), (-2, 0, 0), (-3, 1, 2), (-4, 1, 2), (-4, 1, 2), (-3, 1, 2) },
        new[] { (-1, 0, 0), (-2, 0, 0), (-4, 0, 0), (-4, 0, 0), (-4, 0, 0), (-4, 1, 2), (-2, 1, 2), (-4, 1, 2), (-2, 0, 0), (-2, 1, 1) },
        new[] { (-2, 3, 2), (-1, 0, 0), (-2, 0, 0), (-3, 0, 0), (-3, 1, 2), (-4, 1, 2), (-2, 1, 2), (-3, 0, 0), (-2, 0, 0), (-1, 0, 0) },
        new[] { (-1, 0, 0), (-2, 0, 0), (-2, 0, 0), (-3, 0, 0), (-2, 1, 1), (-4, 1, 2), (-2, 1, 2), (-2, 0, 0), (-2, 0, 0), (-2, 0, 0), (-1, 1, 1) },
        new[] { (-1, 0, 0), (-2, 0, 0), (-3, 0, 0), (-2, 0, 0), (-3, 1, 2), (-4, 0, 0), (-2, 1, 1), (-1, 0, 0), (-2, 0, 0), (-2, 0, 0), (-1, 0, 0) },
        new[] { (-2, 1, 2), (-2, 0, 0), (-3, 0, 0), (-3, 0, 0), (-2, 1, 1), (-2, 0, 0), (-2, 0, 0), (-1, 0, 0), (-2, 0, 0), (-2, 0, 0), (-2, 2, 1) },
        new[] { (-1, 0, 0), (-2, 0, 0), (-2, 0, 0), (-2, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0), (-1, 0, 0) },
        new[] { (-2, 1, 1), (-2, 0, 0), (-2, 0, 0), (-2, 1, 1), (-2, 0, 0), (-1, 0, 0), (-1, 0, 0), (0, 0, 0), (-1, 0, 0), (0, 0, 0) },
        new[] { (-1, 0, 0), (-2, 0, 0), (-2, 0, 0), (-2, 0, 0), (-1, 0, 0), (0, 0, 0), (-1, 0, 0) },
        new[] { (-2, 1, 2), (-1, 0, 0), (-1, 0, 0), (0, 0, 0), (-1, 0, 0), (-1, 0, 0) }
    };

    public static (int ConstructionCost, int Income, int IncomeIfDeveloped) GetCostAndIncomeConfiguration(int rowIndex, int columnIndex)
    {
        return ColumnCostAndIncomeConfiguration[columnIndex][rowIndex - ColumnConfiguration[columnIndex].FirstRow];
    }

    public static readonly (int Row, int Column)[] Forests =
    {
        (8, 6),
        (7, 7), (8, 7),
        (5, 8), (7, 8),
        (2, 9), (3, 9), (4, 9), (5, 9),
        (2, 10), (3, 10), (5, 10), (9, 10),
        (2, 11), (3, 11), (7, 11), (8, 11),
        (1, 12), (2, 12), (3, 12), (7, 12), (8, 12), (9, 12),
        (1, 13), (2, 13), (3, 13), (5, 13), (7, 13),
        (1, 14), (2, 14), (3, 14), (9, 14),
        (1, 15), (2, 15), (3, 15),
        (1, 16), (2, 16), (4, 16),
        (1, 17), (2, 17), (3, 17)
    };

    public static readonly (int Row, int Column) Chicago = (2, 0);
    public static readonly (int Row, int Column) FortWayne = (3, 3);
    public static readonly (int Row, int Column) Detroit = (0, 4);
    public static readonly (int Row, int Column) Wheeling = (6, 8);
    public static readonly (int Row, int Column) Pittsburgh = (4, 10);

    public static readonly (int Row, int Column)[] IndustrialCities =
    {
        Detroit, Wheeling, Pittsburgh
    };

    public static readonly string[] IndustrialCityNames =
    {
        "detroit", "wheeling", "pittsburgh"
    };

    public static readonly int[][] IndustrialCityTrackCells =
    {
        new[] { 1, 2, 3, 4, 5, 6, 7, 8 },
        new[] { 3, 4, 5, 6 },
        new[] { 4, 6, 8 }
    };

    public static readonly (int Row, int Column)[] Cities =
    {
        (2, 0), (5, 1), (2, 2), (3, 3), (7, 3), (1, 5), (5, 5), (8, 5),
        (2, 8), (4, 8), (9, 8), (7, 10), (10, 10), (0, 11),
        (4, 12), (10, 12), (6, 13), (0, 14), (4, 14), (10, 14), (0, 16), (3, 16),
        (0, 18)
    };

    public static readonly (int Row, int Column)[] Mountains =
    {
        (9, 6), (9, 7), (8, 8), (10, 8), (6, 9), (7, 9), (8, 9), (9, 9),
        (6, 10), (8, 10), (4, 11), (5, 11), (6, 11), (5, 12), (6, 12),
        (4, 13)
    };

    public static readonly (int Row, int Column) Wabash_StartField = (3, 3);
    public static readonly (int Row, int Column) PRR_StartField = (5, 17);
    public static readonly (int Row, int Column) BandO_StartField = (7, 16);
    public static readonly (int Row, int Column) NYC_StartField = (3, 18);
    public static readonly (int Row, int Column) CandO_StartField = (9, 16);

    public static readonly (int Row, int Column)[] StartFields =
    {
        PRR_StartField, BandO_StartField, NYC_StartField, CandO_StartField
    };

    public static readonly Dictionary<string, (int Row, int Column)> StartFieldsMap =
        new Dictionary<string, (int Row, int Column)>
        {
            [RailwayCompanies[(int)global::RailwayCompanies.Wabash].Id] = Wabash_StartField,
            [RailwayCompanies[(int)global::RailwayCompanies.PRR].Id] = PRR_StartField,
            [RailwayCompanies[(int)global::RailwayCompanies.BandO].Id] = BandO_StartField,
            [RailwayCompanies[(int)global::RailwayCompanies.NYC].Id] = NYC_StartField,
            [RailwayCompanies[(int)global::RailwayCompanies.CandO].Id] = CandO_StartField
        };

    public static bool FieldIsForest(int rowIndex, int columnIndex) =>
        Forests.Any(field => field.Row == rowIndex && field.Column == columnIndex);

    public static bool FieldIsIndustry(int rowIndex, int columnIndex) =>
        IndustrialCities.Any(field => field.Row == rowIndex && field.Column == columnIndex);

    public static bool FieldIsCity(int rowIndex, int columnIndex) =>
        Cities.Any(field => field.Row == rowIndex && field.Column == columnIndex);

    public static bool FieldIsMountain(int rowIndex, int columnIndex) =>
        Mountains.Any(field => field.Row == rowIndex && field.Column == columnIndex);

    public static bool FieldIsStartField(int rowIndex, int columnIndex) =>
        StartFields.Any(field => field.Row == rowIndex && field.Column == columnIndex);

    public static bool FieldCanContainHouse(int rowIndex, int columnIndex) =>
        FieldIsForest(rowIndex, columnIndex) ||
        FieldIsCity(rowIndex, columnIndex) ||
        FieldIsMountain(rowIndex, columnIndex);

    public static bool FieldExists(int rowIndex, int columnIndex)
    {
        return columnIndex >= 0 &&
               columnIndex < ColumnConfiguration.Length &&
               rowIndex >= ColumnConfiguration[columnIndex].FirstRow &&
               rowIndex <= ColumnConfiguration[columnIndex].LastRow;
    }
}
