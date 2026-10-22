using System;
using Resq.Sim.Rules;

namespace Resq.Sim
{
    public sealed partial class Simulator
    {
        public const int ConsumerInstanceBase = 1000;

        public int RemainingSupplies => D.SupplyBudget - S.C.SupplySpent;

        public int RemainingStock(ToolKind tool) => D.MaxStock[(int)tool] - S.Deployed[(int)tool];

        void ExecuteCommand(Command c)
        {
            Reject r;
            switch (c.Kind)
            {
                case CommandKind.PlaceEquipment: r = Place(c.A, (ToolKind)c.B, c.C); break;
                case CommandKind.MoveIdleEquipment: r = MoveEquipment(c.A, c.B, c.C); break;
                case CommandKind.EnableTool: r = SetToolActive(c.A, true); break;
                case CommandKind.DisableTool: r = SetToolActive(c.A, false); break;
                case CommandKind.AssignGroupRoute: r = AssignGroupRoute(c.A, c.B, c.C == 1); break;
                case CommandKind.AssignVehicleRoute: r = AssignVehicleRoute(c.A, c.B, c.C, c.D, c.E == 1); break;
                case CommandKind.SetPowerPriority: r = SetPriority(c.A, c.B); break;
                case CommandKind.SetGate:
                    if (c.A < 0 || c.A >= S.GateOpen.Length) { r = Reject.UnknownTarget; break; }
                    S.GateOpen[c.A] = c.B != 0; r = Reject.OK; break;
                case CommandKind.EndOperation: r = RequestEnd(c.A == 1); break;
                case CommandKind.AcknowledgeTutorial: r = AcknowledgeTutorial(); break;
                default: r = ExecuteWorldCommand(c); break;
            }
            Emit(SimEventKind.CommandResolved, c.Sequence, (int)c.Kind, r == Reject.OK ? 1 : 0, r);
        }

        // ----------------------------------------------------------------- validation (also used by previews)
        public Reject ValidatePlace(int socket, ToolKind tool, int orientation)
        {
            if (S.C.Phase == Phase.Outcome) return Reject.InvalidPhase;
            if (socket < 0 || socket >= D.SocketIds.Length) return Reject.UnknownTarget;
            if (!D.Permitted[(int)tool]) return Reject.UnknownTarget;
            if (RemainingSupplies < D.ToolCost[(int)tool]) return Reject.InsufficientSupplies;
            if (RemainingStock(tool) <= 0) return Reject.StockExhausted;
            if (D.SocketKind[socket] != tool) return Reject.SocketIncompatible;
            if (((D.SocketOrientations[socket] >> (orientation & 3)) & 1) == 0) return Reject.SocketIncompatible;
            if (S.SocketInstance[socket] >= 0) return Reject.Occupied;
            if (S.C.EquipmentCount >= D.MaxEquipment) return Reject.CapacityFull;
            if (!SocketSafe(socket, tool)) return Reject.UnsafeTarget;
            if (tool == ToolKind.Firebreak) { var fb = ValidateFirebreak(socket); if (fb != Reject.OK) return fb; }
            if (tool == ToolKind.Brace) { var br = ValidateBrace(socket); if (br != Reject.OK) return br; }
            if (tool == ToolKind.Winch)
            {
                if (S.C.VehicleCount >= D.MaxVehicles) return Reject.CapacityFull;
                if (D.SocketSupport[socket] >= 0 && S.SupCollapsed[D.SocketSupport[socket]]) return Reject.UnsafeTarget;
            }
            if (tool == ToolKind.Skiff || tool == ToolKind.Crawler)
            {
                int node = D.SocketNode[socket];
                if (node < 0) return Reject.SocketIncompatible;
                if (S.C.VehicleCount >= D.MaxVehicles) return Reject.CapacityFull;
                if (S.NodeBerthUsed[node] >= Math.Max(1, D.NodeBerths[node])) return Reject.Occupied;
            }
            return Reject.OK;
        }

        bool SocketSafe(int socket, ToolKind tool)
        {
            switch (tool)
            {
                case ToolKind.Generator:
                case ToolKind.Relay:
                    return PlatformDepth(socket) <= EquipmentRules.GeneratorRestartDepthMm;
                case ToolKind.Bridge:
                {
                    // Deck clearance: a bridge edge's max_depth is measured from the channel bed it spans.
                    int e = D.SocketEdge[socket];
                    return e < 0 || MaxEdgeDepth(e) <= D.EdgeMaxDepth[e];
                }
                case ToolKind.Skiff:
                case ToolKind.Barrier:
                case ToolKind.Beacon:
                    return true;
                default:
                    return D.SocketCell[socket] < 0 || Depth(D.SocketCell[socket]) <= EquipmentRules.ElectronicDamageDepthMm;
            }
        }

        long PlatformDepth(int socket)
        {
            var cells = D.SocketPlatformCells[socket];
            long max = 0;
            if (cells != null) foreach (int c in cells) max = Math.Max(max, Depth(c));
            else if (D.SocketCell[socket] >= 0) max = Depth(D.SocketCell[socket]);
            return max;
        }

        Reject Place(int socket, ToolKind tool, int orientation)
        {
            var v = ValidatePlace(socket, tool, orientation);
            if (v != Reject.OK) return v;
            int inst = S.C.EquipmentCount++;
            S.EqKind[inst] = (byte)tool;
            S.EqSocket[inst] = socket;
            S.EqOrientation[inst] = orientation;
            S.EqDurability[inst] = EquipmentRules.Durability;
            S.EqBroken[inst] = false;
            S.EqLatchedOff[inst] = false;
            S.EqRemainder[inst] = 0;
            S.EqPriority[inst] = DefaultPriority(tool);
            // Pumps/sprinklers start enabled when they have a legal intake/outlet; generators start running.
            S.EqActive[inst] = tool == ToolKind.Pump || tool == ToolKind.Generator || tool == ToolKind.Sprinkler || tool == ToolKind.Winch;
            S.SocketInstance[socket] = inst;
            S.Deployed[(int)tool]++;
            S.C.SupplySpent += D.ToolCost[(int)tool];
            if (tool == ToolKind.Firebreak) { ActivateFirebreak(socket); S.EqActive[inst] = false; }
            if (tool == ToolKind.Skiff || tool == ToolKind.Crawler)
            {
                var kind = tool == ToolKind.Skiff ? VehicleKind.Skiff : VehicleKind.Crawler;
                SpawnVehicle(kind, D.SocketNode[socket], tool == ToolKind.Skiff ? 4 : 6, tool == ToolKind.Skiff ? 1 : 2, inst);
            }
            if (tool == ToolKind.Winch) SpawnVehicle(VehicleKind.Winch, D.SocketNode[socket], WinchSeats, 1, inst);
            Emit(SimEventKind.EquipmentPlaced, inst, socket, (int)tool);
            return Reject.OK;
        }

        static int DefaultPriority(ToolKind t)
        {
            switch (t)
            {
                case ToolKind.Winch: return 10;
                case ToolKind.Pump: return 20;
                case ToolKind.Sprinkler: return 30;
                default: return 50;
            }
        }

        public Reject ValidateMove(int inst, int dest, int orientation)
        {
            if (inst < 0 || inst >= S.C.EquipmentCount) return Reject.UnknownTarget;
            if (dest < 0 || dest >= D.SocketIds.Length) return Reject.UnknownTarget;
            var tool = (ToolKind)S.EqKind[inst];
            if (tool == ToolKind.Skiff || tool == ToolKind.Crawler || tool == ToolKind.Brace || tool == ToolKind.Firebreak)
                return Reject.NotIdle;
            if (S.EqBroken[inst]) return Reject.NotIdle;
            if (D.SocketKind[dest] != tool) return Reject.SocketIncompatible;
            int src = S.EqSocket[inst];
            if (D.SocketZone[src] != D.SocketZone[dest]) return Reject.SocketIncompatible;
            if (S.SocketInstance[dest] >= 0) return Reject.Occupied;
            if (((D.SocketOrientations[dest] >> (orientation & 3)) & 1) == 0) return Reject.SocketIncompatible;
            switch (tool)
            {
                case ToolKind.Barrier:
                {
                    foreach (int l in D.SocketLinks[src])
                    {
                        long diff = Math.Abs(Head(D.LinkFrom[l]) - Head(D.LinkTo[l]));
                        if (diff > 100) return Reject.NotIdle;
                    }
                    break;
                }
                case ToolKind.Bridge:
                {
                    int e = D.SocketEdge[src];
                    if (e >= 0 && S.EdgeOccupancy[e] > 0) return Reject.NotIdle;
                    break;
                }
                case ToolKind.Pump:
                case ToolKind.Generator:
                case ToolKind.Sprinkler:
                case ToolKind.Relay:
                    if (S.EqActive[inst]) return Reject.NotIdle;
                    break;
                case ToolKind.Winch:
                {
                    var w = ValidateWinchMove(inst, dest);
                    if (w != Reject.OK) return w;
                    break;
                }
            }
            if (!SocketSafe(dest, tool)) return Reject.UnsafeTarget;
            return Reject.OK;
        }

        Reject MoveEquipment(int inst, int dest, int orientation)
        {
            var v = ValidateMove(inst, dest, orientation);
            if (v != Reject.OK) return v;
            S.SocketInstance[S.EqSocket[inst]] = -1;
            S.EqSocket[inst] = dest;
            S.EqOrientation[inst] = orientation;
            S.SocketInstance[dest] = inst;
            if ((ToolKind)S.EqKind[inst] == ToolKind.Winch) MoveWinchCabin(inst, dest);
            Emit(SimEventKind.EquipmentMoved, inst, dest);
            return Reject.OK;
        }

        Reject SetToolActive(int inst, bool on)
        {
            if (inst < 0 || inst >= S.C.EquipmentCount) return Reject.UnknownTarget;
            if (S.EqBroken[inst]) return Reject.NotIdle;
            var tool = (ToolKind)S.EqKind[inst];
            if (tool == ToolKind.Generator && on)
            {
                if (PlatformDepth(S.EqSocket[inst]) > EquipmentRules.GeneratorRestartDepthMm) return Reject.UnsafeTarget;
                S.EqLatchedOff[inst] = false;
            }
            S.EqActive[inst] = on;
            return Reject.OK;
        }

        Reject SetPriority(int consumer, int priority)
        {
            if (consumer >= ConsumerInstanceBase)
            {
                int inst = consumer - ConsumerInstanceBase;
                if (inst >= S.C.EquipmentCount) return Reject.UnknownTarget;
                S.EqPriority[inst] = priority;
                return Reject.OK;
            }
            if (consumer < 0 || consumer >= D.InfraIds.Length) return Reject.UnknownTarget;
            S.InfraPriority[consumer] = priority;
            return Reject.OK;
        }

        Reject RequestEnd(bool confirmFailure)
        {
            if (!confirmFailure && !TerminalConditionsMet()) return Reject.ObjectivesUnmet;
            S.C.EndRequested = true;
            S.C.EndConfirmedFailure = confirmFailure;
            return Reject.OK;
        }

        Reject AcknowledgeTutorial()
        {
            if (S.C.TutorialStep <= 0) return Reject.InvalidPhase;
            S.C.TutorialStep++;
            return Reject.OK;
        }

        /// <summary>Called by the tutorial controller when the guided bus has reached the depot and the warning was acknowledged.</summary>
        public void EnableScenarioClock()
        {
            if (S.C.ScenarioEnabled) return;
            S.C.ScenarioEnabled = true;
            S.C.OperationStartedEmitted = true;
            S.C.StartedO = S.C.O;
            Emit(SimEventKind.OperationStarted, -1);
            UpdateMilestones();
        }

        // ----------------------------------------------------------------- strain / damage (stage 6)
        void ApplyEquipmentStrain()
        {
            for (int inst = 0; inst < S.C.EquipmentCount; inst++)
            {
                if (S.EqBroken[inst]) continue;
                var tool = (ToolKind)S.EqKind[inst];
                int sock = S.EqSocket[inst];
                bool broke = false;
                switch (tool)
                {
                    case ToolKind.Barrier:
                    {
                        long worst = 0;
                        foreach (int l in D.SocketLinks[sock])
                            worst = Math.Max(worst, Math.Abs(Head(D.LinkFrom[l]) - Head(D.LinkTo[l])));
                        int dur = S.EqDurability[inst];
                        broke = EquipmentRules.BarrierStrain(worst, ref dur);
                        S.EqDurability[inst] = dur;
                        break;
                    }
                    case ToolKind.Bridge:
                    {
                        int e = D.SocketEdge[sock];
                        if (e >= 0 && MaxEdgeDepth(e) > D.EdgeMaxDepth[e] + (EquipmentRules.BridgeDamageDepthMm - EquipmentRules.BridgeCloseDepthMm))
                        {
                            S.EqDurability[inst]--;
                            broke = S.EqDurability[inst] <= 0;
                        }
                        break;
                    }
                    case ToolKind.Skiff:
                    case ToolKind.Brace: // structural, not electronic
                        break;
                    default:
                    {
                        long depth = D.SocketCell[sock] >= 0 ? Depth(D.SocketCell[sock]) : 0;
                        if (tool == ToolKind.Generator || tool == ToolKind.Relay) depth = PlatformDepth(sock);
                        bool hot = D.FireEnabled && D.SocketCell[sock] >= 0 && S.FHeat[D.SocketCell[sock]] >= EquipmentRules.ElectronicDamageHeat;
                        if (tool == ToolKind.Firebreak || tool == ToolKind.Beacon) hot = false;
                        if (depth > EquipmentRules.ElectronicDamageDepthMm || hot)
                        {
                            S.EqDurability[inst]--;
                            broke = S.EqDurability[inst] <= 0;
                        }
                        break;
                    }
                }
                if (broke)
                {
                    S.EqBroken[inst] = true;
                    S.EqActive[inst] = false;
                    S.C.Losses++;
                    Emit(SimEventKind.EquipmentBroken, inst, sock);
                }
            }
        }

        // ----------------------------------------------------------------- power (stage 4 / 6)
        int[] _pnComponent, _pnSupply, _consumerOrder;
        int[] _consumerPriority, _consumerId, _componentSupply;
        ConsumerComparer _consumerComparer;

        void DerivePowerAndInfrastructure()
        {
            int pn = D.PowerNodeCount;
            if (_pnComponent == null || _pnComponent.Length != pn)
            {
                _pnComponent = new int[pn]; _pnSupply = new int[pn];
                int maxConsumers = D.InfraIds.Length + D.MaxEquipment;
                _consumerOrder = new int[maxConsumers]; _consumerPriority = new int[maxConsumers]; _consumerId = new int[maxConsumers];
                _componentSupply = new int[pn];
                _consumerComparer = new ConsumerComparer(_consumerPriority, _consumerId);
            }
            // Generator safety latches (shutdown above 50 mm, latched until explicit restart at <=40 mm).
            for (int inst = 0; inst < S.C.EquipmentCount; inst++)
            {
                if ((ToolKind)S.EqKind[inst] != ToolKind.Generator || S.EqBroken[inst]) continue;
                if (S.EqActive[inst] && !S.EqLatchedOff[inst] && PlatformDepth(S.EqSocket[inst]) > EquipmentRules.GeneratorShutdownDepthMm)
                {
                    S.EqLatchedOff[inst] = true;
                    S.EqActive[inst] = false;
                    S.C.GeneratorShutdowns++;
                    Emit(SimEventKind.GeneratorShutdown, inst);
                }
            }
            // Connected components over enabled links (relay links need an intact, enabled, dry relay).
            for (int i = 0; i < pn; i++) { _pnComponent[i] = i; _pnSupply[i] = D.PowerNodeFixedSupply.Length > i ? D.PowerNodeFixedSupply[i] : 0; }
            for (int l = 0; l < D.PowerLinkFrom.Length; l++)
            {
                int relay = D.PowerLinkRelaySocket[l];
                if (relay >= 0)
                {
                    int inst = S.SocketInstance[relay];
                    if (inst < 0 || S.EqBroken[inst] || PlatformDepth(relay) > EquipmentRules.GeneratorShutdownDepthMm) continue;
                }
                Union(D.PowerLinkFrom[l], D.PowerLinkTo[l]);
            }
            for (int i = 0; i < pn; i++) _pnComponent[i] = Find(i);
            var supply = _componentSupply;
            System.Array.Clear(supply, 0, pn);
            for (int i = 0; i < pn; i++) supply[_pnComponent[i]] += _pnSupply[i];
            for (int inst = 0; inst < S.C.EquipmentCount; inst++)
            {
                if ((ToolKind)S.EqKind[inst] != ToolKind.Generator) continue;
                if (S.EqBroken[inst] || !S.EqActive[inst] || S.EqLatchedOff[inst]) continue;
                int node = D.SocketPowerNode[S.EqSocket[inst]];
                if (node >= 0) supply[_pnComponent[node]] += EquipmentRules.GeneratorSupply;
            }
            // Consumers: infrastructure + powered tools, by (priority, consumer id).
            int count = 0;
            for (int i = 0; i < D.InfraIds.Length; i++)
            {
                if (S.InfraDemand[i] <= 0 || D.InfraPowerNode[i] < 0) continue;
                _consumerId[count] = i; _consumerPriority[count] = S.InfraPriority[i]; count++;
            }
            for (int inst = 0; inst < S.C.EquipmentCount; inst++)
            {
                S.EqPowerGrant[inst] = 0;
                var tool = (ToolKind)S.EqKind[inst];
                if (tool != ToolKind.Pump && tool != ToolKind.Sprinkler && tool != ToolKind.Winch) continue;
                if (S.EqBroken[inst] || !S.EqActive[inst]) continue;
                _consumerId[count] = ConsumerInstanceBase + inst; _consumerPriority[count] = S.EqPriority[inst]; count++;
            }
            for (int i = 0; i < count; i++) _consumerOrder[i] = i;
            Array.Sort(_consumerOrder, 0, count, _consumerComparer);
            for (int i = 0; i < D.InfraIds.Length; i++) S.InfraPowered[i] = S.InfraDemand[i] <= 0;
            for (int k = 0; k < count; k++)
            {
                int c = _consumerOrder[k];
                int id = _consumerId[c];
                int node, demand;
                if (id >= ConsumerInstanceBase)
                {
                    int inst = id - ConsumerInstanceBase;
                    node = D.SocketPowerNode[S.EqSocket[inst]];
                    demand = 1;
                    if (node < 0) continue;
                    int comp = _pnComponent[node];
                    if (supply[comp] >= demand) { supply[comp] -= demand; S.EqPowerGrant[inst] = demand; }
                }
                else
                {
                    node = D.InfraPowerNode[id];
                    demand = S.InfraDemand[id];
                    int comp = _pnComponent[node];
                    if (supply[comp] >= demand) { supply[comp] -= demand; S.InfraPowered[id] = true; }
                }
            }
            // Infrastructure operational state: powered, dry and its required nodes reachable/safe.
            for (int i = 0; i < D.InfraIds.Length; i++)
            {
                bool op = S.InfraPowered[i];
                if (op && D.InfraRegion[i] >= 0 && RegionDepth(D.InfraRegion[i]) > D.InfraMaxDepth[i]) op = false;
                if (op && D.InfraSupport[i] >= 0 && S.SupCollapsed[D.InfraSupport[i]]) op = false;
                if (op && D.FireEnabled && D.InfraRegion[i] >= 0 && MaxRegionHeat(D.InfraRegion[i]) >= D.InfraMaxHeat[i]) op = false;
                if (op && D.InfraRequiredNodes[i] != null)
                {
                    foreach (int n in D.InfraRequiredNodes[i])
                    {
                        if (!DockUsable(n)) { op = false; break; }
                    }
                }
                if (S.InfraEverOperational[i] && S.InfraOperational[i] && !op && D.InfraDemand0[i] > 0) S.C.StationShutdowns++;
                S.InfraOperational[i] = op;
                if (op) S.InfraEverOperational[i] = true;
            }
        }

        int Find(int x)
        {
            while (_pnComponent[x] != x) { _pnComponent[x] = _pnComponent[_pnComponent[x]]; x = _pnComponent[x]; }
            return x;
        }

        void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra == rb) return;
            if (ra < rb) _pnComponent[rb] = ra; else _pnComponent[ra] = rb;
        }

        sealed class ConsumerComparer : System.Collections.Generic.IComparer<int>
        {
            readonly int[] _p, _id;
            public ConsumerComparer(int[] p, int[] id) { _p = p; _id = id; }
            public int Compare(int x, int y)
            {
                int c = _p[x].CompareTo(_p[y]);
                return c != 0 ? c : _id[x].CompareTo(_id[y]);
            }
        }
    }
}
