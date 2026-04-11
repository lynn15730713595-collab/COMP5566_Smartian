namespace EVMAnalysis

open FSharp.Data
open FSharp.Data.JsonExtensions
open B2R2.FrontEnd.BinFile
open B2R2.MiddleEnd.BinEssence

module ABI =

  let rec private parseArgKind (str: string) =
    if str.Contains("[") && str.Contains("]") then
      let idxOpen = str.LastIndexOf("[")
      let idxClose = str.LastIndexOf("]")
      let sizeStr = str.[(idxOpen + 1) .. (idxClose - 1)].Trim()
      let size = SizeType.parse sizeStr
      let remainStr = str.[ .. (idxOpen - 1)] + str.[(idxClose + 1) .. ]
      Array (size, parseArgKind remainStr)
    elif str = "int" then Int 32
    elif str.StartsWith("int") then Int (int str.[3..] / 8)
    elif str = "uint" then UInt 32
    elif str.StartsWith("uint") then UInt (int str.[4..] / 8)
    elif str = "bytes" then Array (UnfixedSize, Byte)
    elif str.StartsWith("bytes") then Array (FixedSize (int str.[5..]), Byte)
    elif str = "address" then Address
    elif str = "bool" then Bool
    elif str = "function" then Array (FixedSize 24, Byte)
    elif str = "string" then String
    else failwithf "Unimplemented type string : %s" str

  let private parsePayable (json: JsonValue) =
    json?stateMutability.AsString() = "payable"

  let private parseArgs (json: JsonValue) =
    json?inputs.AsArray()
    |> Array.map (fun (i: JsonValue) -> i?``type``.AsString())
    |> Array.map (fun str -> { TypeStr = str; Kind = parseArgKind str })

  let parseConstructor (funcJsons: JsonValue list) =
    let isConstr (fJson: JsonValue) = fJson?``type``.AsString() = "constructor"
    match List.tryFind isConstr funcJsons with
    | None -> FuncSpec.DEFAULT_CONSTURCTOR // Constructor may have been omitted.
    | Some json -> FuncSpec.initConstructor (parsePayable json) (parseArgs json)

  let tryResolve essOpt name =
    match essOpt with
    | None -> None
    | Some ess ->
      let symbols = ess.BinHandle.FileInfo.GetSymbols()
      let folder1 accMap (s: Symbol) = Map.add s.Name s.Address accMap
      let nameToAddr = Seq.fold folder1 Map.empty symbols
      Map.tryFind name nameToAddr

  let tryParseFunc essOpt (fJson: JsonValue) =
    let typ = fJson?``type``.AsString()
    if typ = "fallback" then // Empty signature array and no argument.
      let payable = parsePayable fJson
      Some (FuncSpec.initFallback payable)
    elif typ = "function" then
      let name = fJson?name.AsString()
      let payable = parsePayable fJson
      let args = parseArgs fJson
      match tryResolve essOpt name with
      | Some a -> Some (FuncSpec.init name Normal payable a args)
      | _ -> printfn "[WARNING] %s addr unfound in trampoline" name; None
    else None
