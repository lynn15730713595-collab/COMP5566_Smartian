namespace EVMAnalysis

open EVMAnalysis.Domain.Taint
open EVMAnalysis.Domain.AbsVal

/// Information obtained as a result of an analysis.
type FuncInfo = {
  FuncSpec : FuncSpec
  // Variables tainted by the constructor. Read-only for non-constructor funcs.
  ConstrTainted : Set<Variable>
  // Variables defined (i.e. SSTORE) by this function.
  Defs : Set<Variable>
  // Variables used (i.e. SLOAD) by this function.
  Uses : Set<Variable>
}

module FuncInfo =

  let init func constrTainted =
    { FuncSpec = func
      ConstrTainted = constrTainted
      Defs = Set.empty
      Uses = Set.empty }

  let print funcInfo =
    let name = FuncSpec.getName funcInfo.FuncSpec
    let ownerStr = if funcInfo.FuncSpec.OnlyOwner then " (onlyOwner)" else ""
    let defStr = Set.map Variable.toString funcInfo.Defs |> String.concat ", "
    let useStr = Set.map Variable.toString funcInfo.Uses |> String.concat ", "
    printfn "%s:%s Def = { %s }, Use = { %s }" name ownerStr defStr useStr

  // Check if the given taint source set contains the masked value of 'var',
  // which is the variable that is being updated. If so, this must be considered
  // as an update on packed variable.
  let private checkDefVarPacking var taintSet =
    let rec loop taints =
      match taints with
      | [] -> None
      | headTaint :: tailTaints ->
        match var, headTaint with
        | SingleVar (i1, _), (Storage (SingleVar (i2, _), Exclude n))
          when i1 = i2 -> Some n
        | ArrVar (i1, o1, _), (Storage (ArrVar (i2, o2, _), Exclude n))
          when i1 = i2 && o1 = o2 -> Some n
        | MapVar (i1, o1, _), (Storage (MapVar (i2, o2, _), Exclude n))
          when i1 = i2 && o1 = o2 -> Some n
        | _ -> loop tailTaints
    loop (Set.toList taintSet)

  // Find the set of variables defined by update on storage key 'k'. The taint
  // sources of 'v' must be examined to handle variable packing.
  let private findDefs k v =
    let vars = AbsVal.toVariables k
    let taint = AbsVal.getTaint v
    let mapper var =
      match checkDefVarPacking var taint with
      | None -> var
      | Some packOffset -> Variable.setPackOffset var packOffset
    Set.map mapper vars

  // Find the set of variables used at the sink, by examining the taint sources
  // in value 'v'.
  let private findUses v =
    let taint = AbsVal.getTaint v
    let folder acc src =
      match src with
      | Caller | ConstrArg -> acc
      | Storage (var, NoOp) -> Set.add var acc
      | Storage (var, ShiftRight _) -> Set.add var acc
      // If the taint source indicates unpacking of a storage variable, reflect
      // it to 'var' by updating its pack offset.
      | Storage (var, Unpack n) -> Set.add (Variable.setPackOffset var n) acc
      // At high level, we ignore a taint source if it's a variable AND-ed with
      // 0xFF..00..FF pattern mask, because it's for variable update.
      | Storage (var, Exclude _) -> acc
    Set.fold folder Set.empty taint

  // Return true if the 'v' value is tainted by the constructor.
  let private isConstructorTainted funcInfo v =
    let isConstr = (funcInfo.FuncSpec.Kind = Constructor)
    let isCallerTainted = AbsVal.hasTaint Taint.Caller v
    let isArgTainted = AbsVal.hasTaint Taint.ConstrArg v
    isConstr && (isCallerTainted || isArgTainted)

  let recordSStore k v funcInfo =
    let definedVars = findDefs k v
    // Taint sources in both 'k' and 'v' must be considered to compute the use
    // set. Ex) In "m[k] = v", we consider both 'k' and 'v' as taint sinks.
    let usedVars = Set.union (findUses k) (findUses v)
    let constrTainted = if isConstructorTainted funcInfo v
                        then Set.union definedVars funcInfo.ConstrTainted
                        else funcInfo.ConstrTainted
    let defs = Set.union definedVars funcInfo.Defs
    let uses = Set.union usedVars funcInfo.Uses
    { funcInfo with ConstrTainted = constrTainted; Defs = defs; Uses = uses }

  let recordUse v funcInfo =
    let usedVars = findUses v
    let uses = Set.union usedVars funcInfo.Uses
    { funcInfo with Uses = uses }

  let recordCheckSender funcInfo =
    let newFuncSpec = { funcInfo.FuncSpec with OnlyOwner = true }
    { funcInfo with FuncSpec = newFuncSpec }
