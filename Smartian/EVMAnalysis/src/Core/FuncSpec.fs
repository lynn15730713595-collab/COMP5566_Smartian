namespace EVMAnalysis

open EVMAnalysis.Config

type SizeType =
  | FixedSize of int
  | UnfixedSize

module SizeType =

  let parse sizeStr =
    if sizeStr = "" then UnfixedSize else FixedSize (int sizeStr)

  let decideLen = function
    | FixedSize n -> n
    | UnfixedSize -> UNFIXED_ARRAY_INIT_LEN

/// Represents the type as a compile-time fixed size or a dynamic size.
type ArgType =
  | Int of ByteWidth: int
  | UInt of ByteWidth : int
  | Address
  | Bool
  | Byte
  | String
  | Array of (SizeType * ArgType)

type ArgSpec = {
  /// Original string that specifies type. Needed for ABI encoding interface.
  TypeStr : string
  /// Represents the type of element.
  Kind : ArgType
}

module ArgSpec =
  let UInt256 = { TypeStr = "uint256"; Kind = ArgType.UInt 32 }

type FuncKind =
  | Constructor
  | Fallback
  | Normal

module FuncKind =

  let ofString = function
    | "constructor" -> Constructor
    | "fallback" -> Fallback
    | "function" -> Normal
    | _ -> failwith "Invalid function kind string"

type FuncSpec = {
  Name: string
  Kind: FuncKind
  Payable: bool
  OnlyOwner: bool
  Entry: uint64
  ArgSpecs: ArgSpec array
}

module FuncSpec =

  // Note that we handle TX ether value as an argument, as well.

  let initConstructor payable args =
    { Name = "constructor"
      Kind = Constructor
      Payable = payable
      OnlyOwner = false
      Entry = 0UL
      ArgSpecs = Array.append [| ArgSpec.UInt256 |] args }

  let DEFAULT_CONSTURCTOR = initConstructor false [| |]

  let initFallback payable =
    { Name = "fallback"
      Kind = Fallback
      Payable = payable
      OnlyOwner = false
      Entry = 0UL
      ArgSpecs = [| ArgSpec.UInt256 |] }

  let init name kind payable entry args =
    { Name = name
      Kind = kind
      Payable = payable
      OnlyOwner = false
      Entry = entry
      ArgSpecs = Array.append [| ArgSpec.UInt256 |] args }

  let initDummy kind entry =
    { Name = ""
      Kind = kind
      Payable = false
      OnlyOwner = false
      Entry = entry
      ArgSpecs = [| ArgSpec.UInt256; ArgSpec.UInt256 |] }

  let getName func =
    if func.Name <> "" then sprintf "%s" func.Name
    else sprintf "0x%x" func.Entry

type ContractSpec = {
  Constructor : FuncSpec
  NormalFunctions : FuncSpec array
}

module ContractSpec =

  let make constructor normalFuncs =
    { Constructor = constructor; NormalFunctions = normalFuncs }