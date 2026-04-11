module EVMAnalysis.Parse

open System.IO
open FSharp.Data
open B2R2
open B2R2.FrontEnd.BinInterface
open B2R2.MiddleEnd.BinEssence
open B2R2.MiddleEnd.Reclaimer
open EVMAnalysis

let rec private convertHexToBin accBinBytes hexChars =
  match hexChars with
  | [] -> List.rev accBinBytes |> Array.ofList
  | [ _ ] -> failwith "Odd-length byte code provided as input"
  | ch1 :: ch2 :: tailHexChars ->
    let binByte = ("0x" + System.String [|ch1; ch2|]) |> int |> byte
    convertHexToBin (binByte :: accBinBytes) tailHexChars

let private addCFG ess accCFGs entry =
  match CFG.tryBuild ess entry with
  | None -> accCFGs
  | Some cfg -> Map.add entry cfg accCFGs

let getFunctions essOpt abiFile =
  let abiStr = System.IO.File.ReadAllText(abiFile)
  let abiJson = JsonValue.Parse(abiStr)
  let fJsons = [ for v in abiJson -> v ]
  let constructor = ABI.parseConstructor fJsons
  let normalFuncs = List.choose (ABI.tryParseFunc essOpt) fJsons
  (constructor, normalFuncs)

let runWithoutBin abiFile =
  getFunctions None abiFile

let run binFile abiFile =
  let bytes = File.ReadAllText(binFile) |> Seq.toList |> convertHexToBin []
  let hdl = BinHandle.Init(ISA.OfString "evm", bytes)
  // First, construct the CFG of bytecode before the deployment.
  let ess = BinEssence.init hdl
  let preCFGs = Set.fold (addCFG ess) Map.empty ess.CalleeMap.Entries
  // Next, construct the CFG of bytecode after the deployment.
  let passes = [ EVMCodeCopyAnalysis () :> IAnalysis
                 EVMTrampolineAnalysis(abiFile) :> IAnalysis ]
  let ess = Reclaimer.run passes ess
  let postCFGs = Set.fold (addCFG ess) Map.empty ess.CalleeMap.Entries
  // Lastly, obtain the function list to analyze.
  let constructor, normalFuncs = getFunctions (Some ess) abiFile
  (preCFGs, postCFGs, constructor, normalFuncs)
