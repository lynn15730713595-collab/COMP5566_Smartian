namespace EVMAnalysis

open EVMAnalysis.Const

// Temporary registers that appear in IR.
type Register = string

// Code address.
type Addr = uint64

module Addr =

  let toString (addr: Addr) = sprintf "0x%x" addr

  let ofBigInt (i: bigint) =
    if i > MAX_UINT64 then
      printfn "[WARNING] Addr.ofBigInt(0x%s)" (i.ToString("X"))
    uint64 (i &&& MAX_UINT64)

type Subrtn = Addr
module Subrtn = Addr

// Call context.
type Context = Addr list

module Context =

  let toString (ctx: Context) =
    let addrStrs = List.map Addr.toString ctx |> String.concat ", "
    "[" + addrStrs + "]"

// Partitioning index for context- and flow-sensitive analysis.
type PartitionIdx = Context * Addr

module PartitionIdx =

  let toString (idx: PartitionIdx) =
    let ctx, addr = idx
    sprintf "Ctx = %s Addr = %s" (Context.toString ctx) (Addr.toString addr)

// State variables.
type Variable =
  | SingleVar of id: bigint * packOffset: int
  | ArrVar of id: bigint * structOffset: bigint * packOffset: int
  | MapVar of id: bigint * structOffset: bigint * packOffset: int

module Variable =

  let private structOffsetToString o =
    if o = 0I then "" else ".field_" + o.ToString()

  let private packOffsetToString o =
    if o = 0 then "" else ".[" + o.ToString() + ":]"

  let toString = function
    | SingleVar (i, po) -> "var_"  + i.ToString() + packOffsetToString po
    | ArrVar (i, so, po) ->
      let arrPart = "arr_" + i.ToString()
      arrPart + structOffsetToString so + packOffsetToString po
    | MapVar (i, so, po) ->
      let mapPart = "map_" + i.ToString()
      mapPart + structOffsetToString so + packOffsetToString po

  // Set the packing offset of variable 'var' into 'n'.
  let setPackOffset var n =
    match var with
    | SingleVar (i, _) -> SingleVar (i, n)
    | ArrVar (i, so, _) -> ArrVar (i, so, n)
    | MapVar (i, so, _) -> MapVar (i, so, n)


type DUChain = string * Variable * string // Def function * Var * Use function.

module DUChain =

  let toString (chain: DUChain) =
    let defFunc, var, useFunc = chain
    sprintf "%s -- (%s) --> %s" defFunc (Variable.toString var) useFunc
