using Microsoft.Playwright;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace chicago_express_bot
{
    public sealed class game_state_scraping
    {
        public static async Task<GameState> ScrapeGameState(GameFrontendHandler gameFrontendHandler, int playerCount, int playerIndex)
        {
            GameState gameState = new GameState();

            var locator = gameFrontendHandler.GetLocator();

            // scrape player states
            gameState.Players = new List<PlayerState>(playerCount);
            for (int n = 0; n < playerCount; ++n)
            {
                gameState.Players.Add(new PlayerState());
                gameState.Players[n].Shares = new int[ChicagoExpressSetup.RailwayCompanies.Length];

                // determine the cash money of the player
                var banknotes = locator.Locator($"div#player-tableau[data-player=\"{n + 1}\"] div#banknote");
                gameState.Players[n].PlayerCash = await banknotes.CountAsync();

                // determine number of shared held per company
                foreach (var (company, c) in ChicagoExpressSetup.RailwayCompanies.Select(
                    (company, index) => (company, index)))
                {
                    // Locate all matching child divs
                    var stocks = locator.Locator($"div#player-tableau[data-player=\"{n+1}\"] div#{company.Id}-stock");

                    // Determine how many were found
                    gameState.Players[n].Shares[c] = await stocks.CountAsync();
                }
            }

            // scrape company states
            gameState.Companies = new List<CompanyState>(ChicagoExpressSetup.RailwayCompanies.Length);
            foreach (var (company, n) in ChicagoExpressSetup.RailwayCompanies.Select(
                (company, index) => (company, index)))
            {
                gameState.Companies.Add(new CompanyState());

                // Locate all descendant PRR-train, -stock and banknote divs
                var trains = locator.Locator($"div#{company.Id} div#{company.Id}-train");
                var stocks = locator.Locator($"div#{company.Id} div#{company.Id}-stock");
                var banknotes = locator.Locator($"div#{company.Id} div#banknote");

                gameState.Companies[n].Trains = await trains.CountAsync();
                gameState.Companies[n].SharesAvailable = await stocks.CountAsync();
                gameState.Companies[n].Cash = await banknotes.CountAsync();

                var parent = locator.Locator($"xpath=//div[@id='{company.Id}-counter']/ancestor::div[starts-with(@id, 'income-track-')][1]");
                string ? parentId = await parent.GetAttributeAsync("id");
                int income = 0;
                int.TryParse(parentId![(parentId!.LastIndexOf('-') + 1)..], out income);
                gameState.Companies[n].Income = income;
            }

            // scrape the board state
            gameState.Board = new BoardState();
            gameState.Board.NumberOfRemainingHouses = await locator.Locator("#house-pile div#house").CountAsync();

            for (int i = 0; i < ChicagoExpressSetup.Gauges; ++i)
            {
                string gaugeName = string.Empty;
                switch (i)
                {
                    case 0:
                        gaugeName = "auction"; break;
                    case 1:
                        gaugeName = "development"; break;
                    case 2:
                        gaugeName = "route-construction"; break;
                }

                var gauge = locator.Locator($"div#{gaugeName}-gauge");
                string? titleAttribute = await gauge.GetAttributeAsync("title");

                Match match = Regex.Match(titleAttribute!, @"actionAngles:\s*\[([-\d\s,]+)\]");
                int[] actionAngles = match.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();

                var pointer = gauge.Locator("img.gauge-pointer");
                string? style = await pointer.GetAttributeAsync("style");
                // Extract the rotation degree, e.g. "transform: rotate(180deg)"
                match = Regex.Match(style!, @"rotate\(\s*(-?\d+(?:\.\d+)?)deg\s*\)");
                int degree = (int)double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

                gameState.Board.GaugePositions[i] = Array.IndexOf(actionAngles, degree);
            }

            for (int i = 0; i < ChicagoExpressSetup.IndustrialCityNames.Length; ++i)
            {
                var trackField = locator.Locator($"div[id^='{ChicagoExpressSetup.IndustrialCityNames[i]}-']").Filter(new LocatorFilterOptions { Has = locator.Locator("div#house") });

                string? id = await trackField.First.GetAttributeAsync("id");
                Match? match = Regex.Match(id!, $@"^{ChicagoExpressSetup.IndustrialCityNames[i]}-(\d+)$");

                gameState.Board.IndustrialTrackPositions[i] = int.Parse(match!.Groups[1].Value);
            }

            gameState.Board.Fields = new List<BoardField>();
            foreach (var (column, c) in ChicagoExpressSetup.ColumnConfiguration.Select(
                (column, c) => (column, c)))
            {
                for (int r = column.FirstRow; r <= column.LastRow; ++r)
                {
                    var boardField = new BoardField();

                    boardField.DevelopmentPossible = false;
                    boardField.ConstructionCost = 0;
                    boardField.IncomeIncrease = 0;
                    boardField.CashIncrease = 0;

                    var allTrains = locator.Locator($"#hex-{r}-{c} div[id$=\"house\"]");
                    var allTrainCount = await allTrains.CountAsync();

                    if (ChicagoExpressSetup.FieldIsCity(r, c) || ChicagoExpressSetup.FieldIsMountain(r, c))
                    {
                        var house = locator.Locator($"#hex-{r}-{c} div[id$=\"-train\"]");
                        var houseCount = await house.CountAsync();

                        boardField.DevelopmentPossible = houseCount == 0;
                        boardField.IncomeIncrease += boardField.DevelopmentPossible ? ChicagoExpressSetup.GetCostAndIncomeConfiguration(r, c).IncomeIfDeveloped : 0;
                    }
                    else if (ChicagoExpressSetup.FieldIsForest(r, c))
                    {
                        var house = locator.Locator($"#hex-{r}-{c} div[id$=\"-train\"]");
                        var houseCount = await house.CountAsync();

                        boardField.DevelopmentPossible = houseCount == 0;
                        boardField.CashIncrease = boardField.DevelopmentPossible ? 2 : 0;
                    }
                    else if (ChicagoExpressSetup.FieldIsIndustry(r, c))
                    {
                        // if field is an industry possible development depends on position on industrial track
                        if ((ChicagoExpressSetup.Chicago.Row, ChicagoExpressSetup.Chicago.Column) == (r, c))
                        {
                            boardField.DevelopmentPossible = false;
                        }
                        else if ((ChicagoExpressSetup.Pittsburgh.Row, ChicagoExpressSetup.Pittsburgh.Column) == (r, c))
                        {
                            var house = locator.Locator("#pittsburgh-8 div#house");
                            var houseCount = await house.CountAsync();

                            boardField.DevelopmentPossible = houseCount == 0;
                            boardField.IncomeIncrease += boardField.DevelopmentPossible ? 2 : 0;
                        }
                        else if ((ChicagoExpressSetup.Wheeling.Row, ChicagoExpressSetup.Wheeling.Column) == (r, c))
                        {
                            var house = locator.Locator("#wheeling-6 div#house");
                            var houseCount = await house.CountAsync();

                            boardField.DevelopmentPossible = houseCount == 0;
                            boardField.IncomeIncrease += boardField.DevelopmentPossible ? 1 : 0;
                        }
                    }

                    boardField.Companies = new List<FieldCompanyState>();
                    int numCompaniesWhichCanConstruct = 0;
                    foreach (var company in ChicagoExpressSetup.RailwayCompanies)
                    {
                        var fieldCompanyState = new FieldCompanyState();

                        fieldCompanyState.ConstructionPossible = false;
                        if (ChicagoExpressSetup.FieldIsMountain(r, c) || ChicagoExpressSetup.FieldIsForest(r, c))
                        {
                            fieldCompanyState.ConstructionPossible = allTrainCount == 0;

                            if (fieldCompanyState.ConstructionPossible)
                            {
                                boardField.ConstructionCost = ChicagoExpressSetup.GetCostAndIncomeConfiguration(r, c).ConstructionCost * (1 + allTrainCount);
                                ++numCompaniesWhichCanConstruct;
                            }
                        }
                        else if (ChicagoExpressSetup.FieldIsCity(r, c) || ChicagoExpressSetup.FieldIsIndustry(r, c) || !ChicagoExpressSetup.FieldIsStartField(r, c))
                        {
                            var companyTrain = locator.Locator($"#hex-{r}-{c} div#{company.Id}-train");
                            fieldCompanyState.ConstructionPossible = await companyTrain.CountAsync() == 0;

                            if (fieldCompanyState.ConstructionPossible)
                            {
                                boardField.ConstructionCost = ChicagoExpressSetup.GetCostAndIncomeConfiguration(r, c).ConstructionCost * (1 + allTrainCount);
                                ++numCompaniesWhichCanConstruct;
                            }
                        }

                        boardField.Companies.Add(fieldCompanyState);
                    }

                    if (numCompaniesWhichCanConstruct > 0)
                    {
                        boardField.IncomeIncrease += ChicagoExpressSetup.GetCostAndIncomeConfiguration(r, c).Income;
                    }

                    gameState.Board.Fields.Add(boardField);
                }
            }

            return gameState;
        }
    }
}
