using System;
using System.Diagnostics.CodeAnalysis;

namespace chicago_express_bot
{
    public static class EncodeHelper
    {
        public static float Normalize(float value, float max) =>
            Math.Clamp(value / max, 0f, 1f);

        public static void SetOneHot(float[] features, int offset, int count, int index)
        {
            if (index == -1)
                return;

            if (index < 0 || index >= count)
                throw new ArgumentOutOfRangeException(nameof(index));

            features[offset + index] = 1f;
        }
    }

    // TODO! to encode:
    // 1. Game state
    // 2. Decision context (which step of multiple-step turn you are in and what has already been selected)
    // 3. Candidate action

    public static class PlayerStateEncoder
    {
        public static int FeatureCount =>
            1 * ChicagoExpressSetup.RailwayCompanies.Length;

        public static float[] Encode(PlayerState playerState)
        {
            var features = new List<float>(FeatureCount);

            for (int i = 0; i < playerState.Shares.Count(); ++i)
                features.Add(EncodeHelper.Normalize(playerState.Shares[i], ChicagoExpressSetup.RailwayCompanies[i].Stocks));

            if (features.Count != FeatureCount)
                throw new InvalidOperationException(
                    $"Expected {FeatureCount} features, got {features.Count}");

            return features.ToArray();
        }
    }

    public static class CompanyStateEncoder
    {
        public static int FeatureCount => 4;

        public static float[] Encode(CompanyState companyState, int companyIndex)
        {
            var features = new List<float>(FeatureCount);

            features.Add(EncodeHelper.Normalize(companyState.Trains, ChicagoExpressSetup.RailwayCompanies[companyIndex].Trains));
            features.Add(EncodeHelper.Normalize(companyState.Cash, ChicagoExpressSetup.Cash));
            features.Add(EncodeHelper.Normalize(
                companyState.SharesAvailable, ChicagoExpressSetup.RailwayCompanies[companyIndex].Stocks));
            features.Add(EncodeHelper.Normalize(companyState.Income, ChicagoExpressSetup.Cash));

            if (features.Count != FeatureCount)
                throw new InvalidOperationException(
                    $"Expected {FeatureCount} features, got {features.Count}");

            return features.ToArray();
        }
    }

    public static class FieldCompanyStateEncoder
    {
        public static int FeatureCount =>
            1;

        public static float[] Encode(FieldCompanyState fieldCompanyState)
        {
            var features = new List<float>(FeatureCount);

            features.Add(fieldCompanyState.ConstructionPossible ? 1f : 0f);

            // TODO! Check if this is needed
            //features.Add(Normalize(
            //    companyField.IncomeIncrease,
            //    MaxIncomeIncrease));

            if (features.Count != FeatureCount)
                throw new InvalidOperationException(
                    $"Expected {FeatureCount} features, got {features.Count}");

            return features.ToArray();
        }
    }

    public static class BoardFieldEncoder
    {
        public static int FeatureCount =>
            1 // flag if development is possible
            + 1 // construction cost
            + 1 // income increase
            + 1 // cash increase
            + ChicagoExpressSetup.RailwayCompanies.Count() * FieldCompanyStateEncoder.FeatureCount;

        public static float[] Encode(BoardField boardField)
        {
            var features = new List<float>(FeatureCount);

            features.Add(boardField.DevelopmentPossible ? 1f : 0f);
            features.Add(EncodeHelper.Normalize(boardField.ConstructionCost, ChicagoExpressSetup.Cash));
            features.Add(EncodeHelper.Normalize(boardField.IncomeIncrease, ChicagoExpressSetup.Cash));
            features.Add(EncodeHelper.Normalize(boardField.CashIncrease, ChicagoExpressSetup.Cash));

            foreach (var fieldCompanyState in boardField.Companies)
                features.AddRange(FieldCompanyStateEncoder.Encode(fieldCompanyState));

            if (features.Count != FeatureCount)
                throw new InvalidOperationException(
                    $"Expected {FeatureCount} features, got {features.Count}");

            return features.ToArray();
        }
    }

    public sealed class GameStateEncoder
    {
        private readonly int _playerCount;
        //private readonly int _gaugeSize;

        public GameStateEncoder(int playerCount)
        {
            _playerCount = playerCount;
            //_gaugeSize = 3;
        }

        public int FeatureCount =>
            1 // cash of bot player
            + _playerCount * PlayerStateEncoder.FeatureCount
            + ChicagoExpressSetup.RailwayCompanies.Length * CompanyStateEncoder.FeatureCount
            //+ _gaugeSize
            + ChicagoExpressSetup.FieldCount() * BoardFieldEncoder.FeatureCount;


        public float[] Encode(GameState state)
        {
            Validate(state);

            var features = new List<float>(FeatureCount);

            // 0. Bot player cash
            features.Add(EncodeHelper.Normalize(state.CurrentPlayerCash, ChicagoExpressSetup.Cash));

            // 1. Shares: acting player first, then other players.
            for (int offset = 0; offset < _playerCount; offset++)
            {
                int playerIndex =
                    (state.CurrentPlayer + offset) % _playerCount;

                features.AddRange(PlayerStateEncoder.Encode(state.Players[playerIndex]));
            }

            // 2. Company features.
            for (int i = 0; i < state.Companies.Count; ++i)
            {
                features.AddRange(CompanyStateEncoder.Encode(state.Companies[i], i));
            }

            // 3. Action gauge. TOOD! consider to encode gauge positions
            //features.AddRange(state.ActionGauge);

            // 4. Board features, in permanent field order.
            foreach (var field in state.Board)
            {
                features.AddRange(BoardFieldEncoder.Encode(field));
            }

            if (features.Count != FeatureCount)
                throw new InvalidOperationException(
                    $"Expected {FeatureCount} features, got {features.Count}");

            return features.ToArray();
        }

        private void Validate(GameState state)
        {
            if (state.Players.Count != _playerCount)
                throw new ArgumentException("Unexpected player count.");

            if (state.Companies.Count != ChicagoExpressSetup.RailwayCompanies.Length)
                throw new ArgumentException("Unexpected company count.");

            if (state.Board.Count != ChicagoExpressSetup.FieldCount())
                throw new ArgumentException("Unexpected field count.");

            if (state.CurrentPlayer < 0 ||
                state.CurrentPlayer >= _playerCount)
                throw new ArgumentException("Invalid current player.");

            // TODO! consider to encode gauge positions
            //if (state.ActionGauge.Length != _gaugeSize)
            //    throw new ArgumentException("Unexpected gauge size.");

            foreach (var player in state.Players)
            {
                if (player.Shares.Length != ChicagoExpressSetup.RailwayCompanies.Length)
                    throw new ArgumentException(
                        "Unexpected player share-array size.");
            }

            foreach (var field in state.Board)
            {
                if (field.Companies.Count != ChicagoExpressSetup.RailwayCompanies.Length)
                    throw new ArgumentException(
                        "Unexpected company count on field.");
            }
        }
    }

    public static class DecisionContextEncoder
    {
        public static int FeatureCount =>
            Enum.GetValues<DecisionPhase>().Length
            + ChicagoExpressSetup.Gauges // selected gauge
            + ChicagoExpressSetup.RailwayCompanies.Length // selected company
            + 1 // current bid
        ;

        public static float[] Encode(DecisionContext decisionContext)
        {
            var result = new float[FeatureCount];

            int phaseOffset = 0;
            int gaugeOffset = phaseOffset + Enum.GetValues<DecisionPhase>().Length;
            int companyOffset = gaugeOffset + ChicagoExpressSetup.Gauges;
            int bidOffset = companyOffset + ChicagoExpressSetup.RailwayCompanies.Length;

            EncodeHelper.SetOneHot(result, phaseOffset, Enum.GetValues<DecisionPhase>().Length, (int)decisionContext.Phase);
            EncodeHelper.SetOneHot(result, gaugeOffset, ChicagoExpressSetup.Gauges, decisionContext.SelectedGauge);
            EncodeHelper.SetOneHot(result, companyOffset, ChicagoExpressSetup.RailwayCompanies.Length, decisionContext.SelectedCompany);
            result[bidOffset] = Math.Clamp(decisionContext.CurrentHighBid / 100f, 0f, 1f);

            return result.ToArray();
        }
    }

    public static class ActionEncoder
    {
        public static int FeatureCount =>
            Enum.GetValues<ActionType>().Length
            + ChicagoExpressSetup.Gauges
            + ChicagoExpressSetup.RailwayCompanies.Length
            + ChicagoExpressSetup.RowCount()
            + ChicagoExpressSetup.ColumnCount()
            + 1; // One additional feature for a normalized bid.

        public static float[] Encode(CandidateAction action)
        {
            var result = new float[FeatureCount];

            int typeOffset = 0;
            int gaugeOffset = typeOffset + Enum.GetValues<ActionType>().Length;
            int companyOffset = gaugeOffset + ChicagoExpressSetup.Gauges;
            int rowOffset = companyOffset + ChicagoExpressSetup.RailwayCompanies.Length;
            int columnOffset = rowOffset + ChicagoExpressSetup.RowCount();
            int bidOffset = columnOffset + ChicagoExpressSetup.ColumnCount();

            EncodeHelper.SetOneHot(result, typeOffset, Enum.GetValues<ActionType>().Length, (int)action.Type);
            if (action is SelectGaugeAction)
                EncodeHelper.SetOneHot(result, gaugeOffset, ChicagoExpressSetup.Gauges, ((SelectGaugeAction) action).GaugeIndex);
            if (action is SelectCompanyAction)
                EncodeHelper.SetOneHot(result, companyOffset, ChicagoExpressSetup.RailwayCompanies.Length, ((SelectCompanyAction) action).CompanyIndex);
            if (action is SelectFieldAction)
            {
                EncodeHelper.SetOneHot(result, rowOffset, ChicagoExpressSetup.RowCount(), ((SelectFieldAction) action).RowIndex);
                EncodeHelper.SetOneHot(result, columnOffset, ChicagoExpressSetup.ColumnCount(), ((SelectFieldAction) action).ColumnIndex);
            }
            // TODO! consider to change encoding of bid amount
            if (action is BidAction)
                result[bidOffset] = Math.Clamp(((BidAction) action).BidAmount / 100f, 0f, 1f);

            return result;
        }
    }

    public sealed class StateEncoder
    {
        private GameStateEncoder gameStateEncoder_;
        public int FeatureCount =>
            gameStateEncoder_.FeatureCount
            + DecisionContextEncoder.FeatureCount;

        public StateEncoder(int playerCount)
        {
            gameStateEncoder_ = new GameStateEncoder(playerCount);
        }

        public float[] Encode(GameState gameState, DecisionContext decisionContext)
        {
            var features = new List<float>(FeatureCount);

            features.AddRange(gameStateEncoder_.Encode(gameState));
            features.AddRange(DecisionContextEncoder.Encode(decisionContext));

            if (features.Count != FeatureCount)
                throw new InvalidOperationException(
                    $"Expected {FeatureCount} features, got {features.Count}");

            return features.ToArray();
        }
    }
}
