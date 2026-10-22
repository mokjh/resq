using System;
using System.Collections.Generic;
using System.Linq;

namespace Resq.Content
{
    public static partial class ContentCompilation
    {
        private sealed class TraversalState
        {
            public readonly string Node,Mode;
            public readonly TraversalState Previous;
            public readonly NavEdge Via;
            public TraversalState(string node,string mode,TraversalState previous,NavEdge via) { Node=node;Mode=mode;Previous=previous;Via=via; }
        }
        private sealed class StructuralSearch
        {
            public readonly HashSet<string> Reachable=new HashSet<string>(StringComparer.Ordinal);
            public NavEdge[] Path;
        }
        private static StructuralSearch Traverse(string start,string traversal,string mobility,IEnumerable<NavEdge> edgeSource,IDictionary<string,Socket> sockets,IDictionary<string,EquipmentDef> equipment,CompiledMission mission,LayoutDef layout,HashSet<string> targets,bool inTransit=false)
        {
            var nodes=layout.nav_nodes.ToDictionary(item=>item.id,item=>item,StringComparer.Ordinal);
            var outgoing=edgeSource.GroupBy(item=>item.from_node_id).ToDictionary(group=>group.Key,group=>group.OrderBy(item=>item.id,StringComparer.Ordinal).ToArray(),StringComparer.Ordinal);
            var result=new StructuralSearch();var visited=new HashSet<string>(StringComparer.Ordinal);var queue=new Queue<TraversalState>();
            Action<string,string,TraversalState,NavEdge> enqueue=(node,mode,previous,via)=> { if(visited.Add(mode+"|"+node)) queue.Enqueue(new TraversalState(node,mode,previous,via)); };
            if(inTransit&&(traversal=="water"||traversal=="cable")&&(nodes[start].vehicle_slots>0||nodes[start].dock_berths>0)) enqueue(start,traversal,null,null);
            if(nodes[start].resident_capacity>0&&nodes[start].mobility_classes.Contains(mobility)) enqueue(start,"walk",null,null);
            foreach(var vehicle in layout.fixed_vehicles.Where(item=>item.node_id==start&&item.initial_occupant_ids.Count>0)) enqueue(start,vehicle.kind=="skiff"?"water":vehicle.kind=="winch"?"cable":"vehicle_ground",null,null);
            Func<string,long,bool> transport=(mode,slots)=>
            {
                string tool=mode=="water"?"skiff":mode=="cable"?"winch":"crawler";
                if(layout.fixed_vehicles.Any(vehicle=>(mode=="vehicle_ground"?(vehicle.kind=="fixed_bus"||vehicle.kind=="crawler"):vehicle.kind==tool)&&vehicle.seats>0&&vehicle.movement_slots<=slots)) return true;
                return equipment.ContainsKey(tool)&&equipment[tool].resident_seats>0&&equipment[tool].movement_slots<=slots&&mission.maximum_stock.Any(stock=>stock.equipment_id==tool&&stock.count>0)&&layout.sockets.Any(socket=>socket.enabled&&socket.permitted_equipment_ids.Contains(tool));
            };
            while(queue.Count>0)
            {
                var state=queue.Dequeue();var node=nodes[state.Node];
                if(!node.mobility_classes.Contains(mobility)) continue;
                bool canUnload=node.resident_capacity>0&&(state.Mode=="water"?node.dock_berths>0:node.vehicle_slots>0||node.dock_berths>0);
                if(state.Mode!="walk"&&canUnload) enqueue(state.Node,"walk",state,null);
                if(state.Mode=="walk"||(inTransit&&(state.Mode==traversal||(state.Mode=="vehicle_ground"&&traversal=="ground"))))
                {
                    result.Reachable.Add(state.Node);
                    if(result.Path==null&&targets!=null&&targets.Contains(state.Node))
                    {
                        var path=new List<NavEdge>();for(var cursor=state;cursor!=null;cursor=cursor.Previous) if(cursor.Via!=null) path.Add(cursor.Via);path.Reverse();result.Path=path.ToArray();
                    }
                }
                if(!outgoing.ContainsKey(state.Node)) continue;
                foreach(var edge in outgoing[state.Node])
                {
                    if(!edge.administrative_enabled||!edge.mobility_classes.Contains(mobility)||(traversal!="all"&&edge.traversal_class!=traversal)) continue;
                    var destination=nodes[edge.to_node_id];if(!destination.mobility_classes.Contains(mobility)) continue;
                    if(edge.required_equipment_socket_id!=null)
                    {
                        var socket=sockets[edge.required_equipment_socket_id];
                        if(!socket.enabled||!socket.permitted_equipment_ids.Any(id=>equipment.ContainsKey(id)&&mission.maximum_stock.Any(stock=>stock.equipment_id==id&&stock.count>0)&&equipment[id].supply_cost<=mission.supply_budget)) continue;
                    }
                    if(edge.traversal_class=="ground"&&state.Mode=="walk"&&node.resident_capacity>0&&destination.resident_capacity>0) enqueue(edge.to_node_id,"walk",state,edge);
                    string mode=edge.traversal_class=="ground"?"vehicle_ground":edge.traversal_class;
                    bool canBoard=state.Mode=="walk"&&node.resident_capacity>0&&(mode=="water"?node.dock_berths>0:node.vehicle_slots>0);
                    bool canContinue=state.Mode==mode;
                    if(!(canBoard||canContinue)||!transport(mode,edge.movement_capacity)) continue;
                    if((node.vehicle_slots<1&&node.dock_berths<1)||(destination.vehicle_slots<1&&destination.dock_berths<1)) continue;
                    enqueue(edge.to_node_id,mode,state,edge);
                }
            }
            return result;
        }
    }
}
