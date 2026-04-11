module EVMAnalysis.Domain.Taint

open EVMAnalysis

// Represents the kind of operation that is performed on a value tainted by
// storage variables.
type Operation =
  // No operation applied yet.
  | NoOp
  // Means AND operation to extract the packed variable at offset 'n'.
  | Unpack of n: int
  // Means AND operation to exclude the packed variable at offset 'n'.
  | Exclude of n: int
  // Shift to right by 'n' bytes.
  | ShiftRight of n: int

module Operation =
  // Recognize how many 'b's consecutively appears as the prefix of 'bytes'.
  // Return (1) the number of identified 'b' and (2) the remaining suffix.
  let rec private countPrefixFromBytes b bytes =
    match bytes with
    | [] -> (0, [])
    | headByte :: tailBytes ->
      if b <> headByte then (0, bytes)
      else
        let (n, suffix) = countPrefixFromBytes b tailBytes
        (n + 1, suffix)

  // Convert mask to list of 32 bytes. MSB must come in the head.
  let private maskToByteList (mask: bigint) =
    let bytes = mask.ToByteArray()
    let bytes = // Pad or truncate to 32-bytes.
      if bytes.Length >= 32 then bytes.[0 .. 31]
      else Array.append bytes (Array.init (32 - bytes.Length) (fun _ -> 0uy))
    Array.rev bytes |> Array.toList

  // Check 'mask' whether it has the pattern of 0x0000..FF..00 (a mask used for
  // extracting a packed variable). If so, return the starting offset of 'FF'
  // counted from the right side.
  let findUnpackFromAnd (mask: bigint) =
    let bytes = maskToByteList mask
    let n1, bytes = countPrefixFromBytes 0uy bytes
    let n2, bytes = countPrefixFromBytes 255uy bytes
    let n3, _ = countPrefixFromBytes 0uy bytes
    // First, all the bytes must be 0xFF or 0x00, and 0xFF byte must exist.
    if n1 + n2 + n3 = 32 && n2 > 0 then
      // Next, if 'n1' and 'n3' are not zero, it's a mask we're looking for.
      if (n1 > 0 && n3 > 0) then Some n3
      // If not, conservatively check if 'n2' is size of widely used types.
      elif List.contains n2 [1; 2; 4; 8; 16; 20] then Some n3
      else None
    else None

  // Check 'mask' whether it has the pattern of 0xFFFF..00..FF (a mask used for
  // updating a packed variable). If so, return the starting offset of '00'
  // counted from the right side.
  let findExcludeFromAnd mask =
    let bytes = maskToByteList mask
    let n1, bytes = countPrefixFromBytes 255uy bytes
    let n2, bytes = countPrefixFromBytes 0uy bytes
    let n3, _ = countPrefixFromBytes 255uy bytes
    // First, all the bytes must be 0xFF or 0x00, and 0x00 byte must exist.
    if n1 + n2 + n3 = 32 && n2 > 0 then
      // Next, if 'n1' and 'n3' are not zero, it's a mask we're looking for.
      if (n1 > 0 && n3 > 0) then Some n3
      // If not, conservatively check if 'n2' is size of widely used types.
      elif List.contains n2 [1; 2; 4; 8; 16; 20] then Some n3
      else None
    else None

  // Check if division with 'i' can be considered as an extraction of packed
  // variable. For example, division with (0x100 ^ n) can be thougt as shift
  // operation that extracts bytes at offset 'n', counted from the right side.
  let rec findShiftFromDiv (i: bigint) =
    if i < 256I then None
    elif i = 256I then Some 1
    elif i % 256I <> 0I then None
    else
      match findShiftFromDiv (i / 256I) with
      | Some n -> Some (n + 1)
      | None -> None

type Source =
  | Caller  // EVM's caller opcode.
  | ConstrArg
  | Storage of Variable * Operation

type SourceModule () =
  inherit Elem<Source>()
  override __.toString src =
    match src with
    | Caller -> "CALLER"
    | ConstrArg -> "AndExtract_ARG"
    | Storage (var, NoOp) -> Variable.toString var
    | Storage (var, Unpack n) -> sprintf "%s[%d:]" (Variable.toString var) n
    | Storage (var, Exclude n) -> sprintf "^%s[%d:]" (Variable.toString var) n
    | Storage (var, ShiftRight n) -> sprintf "^%s[%d:]" (Variable.toString var) n

  member __.AndWithConst i src =
    match src with
    | Caller -> Caller
    | ConstrArg -> ConstrArg
    | Storage (var, NoOp) ->
      match Operation.findUnpackFromAnd i, Operation.findExcludeFromAnd i with
      | Some o, _ -> Storage (var, Unpack o)
      | _, Some o -> Storage (var, Exclude o)
      | None, None -> Storage (var, NoOp)
    | Storage (var, ShiftRight n) ->
      match Operation.findUnpackFromAnd i with
      | Some o -> Storage (var, Unpack (n + o))
      | None -> Storage (var, Unpack n)
    | Storage (var, op) -> Storage (var, op)

  member __.DivWithConst i src =
    match src with
    | Caller -> Caller
    | ConstrArg -> ConstrArg
    | Storage (var, NoOp) ->
      match Operation.findShiftFromDiv i with
      | Some o -> Storage (var, Unpack o)
      | None -> Storage (var, NoOp)
    | Storage (var, op) -> Storage (var, op)

let Source = SourceModule () // Use 'Source' like a module.

type Taint = Set<Source>

type TaintModule () =
  inherit SetDomain<Source>(Source)

  member __.Caller: Taint = Set.singleton Caller

  member __.ConstrArg: Taint = Set.singleton ConstrArg

  member __.ofVars = Set.map (fun v -> Storage (v, NoOp))

  member __.AndWithConst i t =
    Set.map (Source.AndWithConst i) t

  member __.DivWithConst i t =
    Set.map (Source.DivWithConst i) t

let Taint = TaintModule() // Use 'Taint' like a module.
