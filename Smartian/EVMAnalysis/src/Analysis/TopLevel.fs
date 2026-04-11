module EVMAnalysis.TopLevel

open EVMAnalysis

type Sequence = string list

let private analyzeConstructor cfgs constrFunc =
  let constrInfo = AbstractInterpret.run cfgs Set.empty constrFunc
  let constrTainted = constrInfo.ConstrTainted
  let taintStr = Set.map Variable.toString constrTainted |> String.concat ", "
  FuncInfo.print constrInfo // DEBUG
  printfn "Constructor tainted: { %s }" taintStr // DEBUG
  constrTainted

let private analyzeNormalFuncs cfgs constrTainted funcs =
  let mapper func =
    let funcInfo = AbstractInterpret.run cfgs constrTainted func
    FuncInfo.print funcInfo // DEBUG
    funcInfo
  List.map mapper funcs

// Return the list of function pairs (f, g) that can have any def-use chain.
let enumerateDUChains funcs funcInfoMap =
  let folder acc g =
    List.fold (fun acc' f ->
      let gInfo = Map.find g funcInfoMap
      let fInfo = Map.find f funcInfoMap
      let fDefs, gUses = fInfo.Defs, gInfo.Uses
      let interSect = Set.intersect fDefs gUses
      if Set.isEmpty interSect then acc' else Set.add (f, g) acc'
    ) acc funcs
  List.fold folder Set.empty funcs

// Return the set of def-use chains found in the given sequence 'funcSeq'.
let private evalDUChain funcInfoMap (funcSeq: Sequence): Set<DUChain> =
  let folder (accChains, accDefMap) f =
    let funcInfo = Map.find f funcInfoMap
    let defs = funcInfo.Defs
    let uses = funcInfo.Uses
    let chooser useVar =
      match Map.tryFind useVar accDefMap with
      | None -> None
      | Some defFunc -> Some (defFunc, useVar, f)
    let accChains = Set.union accChains (Set.choose chooser uses)
    // Approximate that 'f' always updates 'defs', to avoid too long sequence.
    let folder acc defVar = Map.add defVar f acc
    let accDefMap = Set.fold folder accDefMap defs
    (accChains, accDefMap)
  List.fold folder (Set.empty, Map.empty) funcSeq |> fst

// Extend 'seq' as much as possible as long as there is a gain in DU chain.
let private extendSequence funcInfoMap accChains seq =
  // Try a new sequence obtained by appending each function after 'seq'.
  let rec appendLoop funcs accChains seq =
    match funcs with
    | [] -> None
    | headFunc :: tailFuncs ->
      let duChains = evalDUChain funcInfoMap (seq @ [headFunc])
      if Set.isEmpty (Set.difference duChains accChains)
      then appendLoop tailFuncs accChains seq
      else Some (seq @ [headFunc])
  // Run recursively until there is no more gain in accumulative DU chain set.
  let rec extendLoop accChains s =
    let allFuncs = Map.keys funcInfoMap
    match appendLoop allFuncs accChains s with
    | None -> (accChains, s)
    | Some newSeq ->
      let accChains = Set.union accChains (evalDUChain funcInfoMap newSeq)
      extendLoop accChains newSeq
  extendLoop accChains seq

// Test if function 'f' is already included in one of 'seqs'.
let isSubsumed f seqs =
  List.exists (fun seq -> List.contains f seq) seqs

let rec private buildLoop funcInfoMap (accChains, accSeqs) funcs =
  match funcs with
  | [] -> accSeqs
  | headFunc :: tailFuncs when isSubsumed headFunc accSeqs ->
    buildLoop funcInfoMap (accChains, accSeqs) tailFuncs
  | headFunc :: tailFuncs ->
    let accChains, newSeq = extendSequence funcInfoMap accChains [headFunc]
    let accSeqs = newSeq :: accSeqs
    buildLoop funcInfoMap (accChains, accSeqs) tailFuncs

let parseABI abiFile =
  let constrFunc, normalFuncs = Parse.runWithoutBin abiFile
  ContractSpec.make constrFunc (Array.ofList normalFuncs)

let printStats funcInfoMap seqs =
  let funcs = Map.keys funcInfoMap
  let duChains = enumerateDUChains funcs funcInfoMap
  let numChains = Set.count duChains
  let numSeeds = List.length seqs
  let sumOfLen = List.fold (fun acc s -> acc + List.length s) 0 seqs
  let avgLen = float sumOfLen / float numSeeds
  printfn "================== < Def-Use Chain > =================="
  let _ = Set.iter (fun chain -> printfn "%A" chain) duChains
  printfn "==================  < Candidate Sequences > =================="
  List.iter (fun seq -> printfn "%A" seq) seqs
  printfn "Number of def-use chains: %d" numChains
  printfn "Number of seeds: %d" numSeeds
  printfn "Avg length of seeds: %.1f" avgLen

let parseAndAnalyze binFile abiFile =
  // Parse and statically analyze bytecode.
  let preCFGs, postCFGs, constrFunc, normalFuncs = Parse.run binFile abiFile
  let constrTainted = analyzeConstructor preCFGs constrFunc
  let funcInfos = analyzeNormalFuncs postCFGs constrTainted normalFuncs
  // Next, generate ContractSpec to return. Should recompute 'normalFuncs' by
  // extracting from 'funcInfos', to reflect the updates from static analysis.
  let normalFuncs = List.map (fun info -> info.FuncSpec) funcInfos
  let contractSpec = ContractSpec.make constrFunc (Array.ofList normalFuncs)
  // Now, decide transaction sequence order with the analysis result.
  let folder accMap info = Map.add (FuncSpec.getName info.FuncSpec) info accMap
  let funcInfoMap = List.fold folder Map.empty funcInfos
  let defOnlys, defAndUses =
    List.filter (fun i -> not (Set.isEmpty i.Defs)) funcInfos
    |> List.partition (fun i -> Set.isEmpty i.Uses)
  let startFuncs =
    List.map (fun i -> FuncSpec.getName i.FuncSpec) (defOnlys @ defAndUses)
  let seqs = buildLoop funcInfoMap (Set.empty, []) startFuncs
  printStats funcInfoMap seqs
  (contractSpec, seqs)