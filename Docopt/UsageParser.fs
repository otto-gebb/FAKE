﻿namespace Docopt
#nowarn "62"
#light "off"

open FParsec
open System
open System.Text

module USPH =
  begin
    let inline λ f a b = f (a, b)
    let inline Λ f (a, b) = f a b

    type 'a GList = System.Collections.Generic.List<'a>
    [<NoComparison>]
    type Optpack =
      struct
        val Sopt : GList<char * int>
        val Lopt : GList<string * int>
        member xx.MatchSopt(sopt:char) =
          let idx = xx.Sopt.FindIndex(fun (s', _) -> s' = sopt) in
          if idx = -1 then true
          else match xx.Sopt.[idx] with
                 | _, 1  -> let _ = xx.Sopt.RemoveAt(idx) in false
                 | _, -2 -> false
                 | c, n  -> let _ = xx.Sopt.[idx] <- (c, n - 1) in false
        member xx.MatchLopt(lopt:string) =
          let idx = xx.Lopt.FindIndex(fun (s', _) -> s' = lopt) in
          if idx = -1 then true
          else match xx.Lopt.[idx] with
                 | _, 1  -> let _ = xx.Lopt.RemoveAt(idx) in false
                 | _, -2 -> false
                 | s, n  -> let _ = xx.Lopt.[idx] <- (s, n - 1) in false
      end

    [<NoComparison>]
    type Ast =
      | Arg of string      // Argument
      | Sop of string      // Short option pack (not in ast)
      | Lop of string      // Long option (not in ast)
      | Opt of Optpack     // Option pack, combines all Sop and Lop
      | Cmd of string      // Command
      | Xor of (Ast * Ast) // Mutual exclusion (| operator)
      | Ell of Ast         // One or more (... operator)
      | Req of Ast         // Required (between parenthesis)
      | Sqb of Ast         // Optional (between square brackets)
      | Ano                // Any options `[options]`
      | Dsh                // Double dash `[--]` (Bowser team > all)
      | Ssh                // Single dash `[-]`
      | Seq of Ast array   // Sequence of Ast's
      | Eps                // Epsilon parser
      with
        static member Reduce = function
          | a -> a
      end

    let opp = OperatorPrecedenceParser<_, unit, unit>()
    let pupperArg =
      let start c' = isUpper c' || isDigit c' in
      let cont c' = start c' || c' = '-' in
      identifier (IdentifierOptions(isAsciiIdStart=start,
                                    isAsciiIdContinue=cont,
                                    label="UPPER-CASE identifier"))
    let plowerArg =
      satisfyL (( = ) '<') "<lower-case> identifier"
      >>. many1SatisfyL (( <> ) '>') "any character except '>'"
      .>> pchar '>'
      |>> (fun name' -> String.Concat("<", name', ">"))
    let parg = pupperArg <|> plowerArg
    let plop = 
      let predicate c' = isLetter(c') || isDigit(c') || c' = '-' || c' = '_'
      in skipString "--" >>. many1Satisfy predicate
    let psop =
      skipChar '-'
      >>. many1Satisfy (isLetter)
    let pcmd = many1Satisfy (fun c' -> isLetter(c') || isDigit(c'))
    let preq = between (pchar '(' >>. spaces) (pchar ')') opp.ExpressionParser
    let psqb = between (pchar '[' >>. spaces) (pchar ']') opp.ExpressionParser
    let pano = skipString "[options]"
    let pdsh = skipString "[--]"
    let pssh = skipString "[-]"
    let term = choice [|
                        pano >>% Ano;
                        pdsh >>% Dsh;
                        pssh >>% Ssh;
                        psqb |>> Sqb;
                        preq |>> Req;
                        plop |>> Lop;
                        psop |>> Sop;
                        parg |>> Arg;
                        pcmd |>> Cmd;
                        |]
    let pxor = InfixOperator("|", spaces, 10, Associativity.Left, λ Xor)
    let pell = let makeEll = function
                 | Seq(seq) as ast -> let cell = seq.[seq.Length - 1] in
                                      (seq.[seq.Length - 1] <- Ell(cell); ast)
                 | ast             -> Ell(ast)
               in PostfixOperator("...", spaces, 20, false, makeEll)
    let _ = opp.TermParser <- sepEndBy1 term spaces1
                              |>> function
                                    | []    -> Eps
                                    | [ast] -> ast
                                    | asts  -> Seq(List.toArray asts)
    let _ = opp.AddOperator(pxor)
    let _ = opp.AddOperator(pell)
    let pusageLine = spaces
                     >>. opp.ExpressionParser
                     |>> Ast.Reduce
  end
;;

open USPH
module Err = FParsec.Error

exception UsageException of string
exception ArgvException of string

type UsageParser(u':string, opts':Options) =
  class
    let parseAsync = function
      | ""   -> async {
          return Eps
        }
      | line -> async {
          let line = line.TrimStart() in
          let index = line.IndexOfAny([|' ';'\t'|]) in
          return if index = -1 then Eps
                 else match run pusageLine (line.Substring(index)) with
                        | Success(res, _, _) -> res
                        | Failure(err, _, _) -> raise (UsageException(err))
        }
    let ast = u'.Split([|'\n';'\r'|], StringSplitOptions.RemoveEmptyEntries)
              |> Array.map parseAsync
              |> Async.Parallel
              |> Async.RunSynchronously
              |> function
                   | [||]    -> Eps
                   | [|ast|] -> ast
                   | asts    -> Array.reduce (λ Xor) asts
    let i = ref 0
    let len = ref 0
    let argv = ref<string array> null
    let args = ref<Arguments.Dictionary> null
    let carg () = let argv = !argv in argv.[!i]
    let rec eval = function
      | Arg(arg) -> farg arg
      | Opt(opt) -> fopt opt
      | Cmd(cmd) -> fcmd cmd
      | Xor(xor) -> fxor xor
      | Ell(ast) -> fell ast
      | Req(ast) -> freq ast
      | Sqb(ast) -> fsqb ast
      | Ano      -> fano ( )
      | Dsh      -> fdsh ( )
      | Ssh      -> fssh ( )
      | Seq(seq) -> fseq seq
      | Eps      -> feps ( )
      | _        -> Some(Err.otherError "Error in AST")
    and farg arg' = None
    and fopt opt' = None
    and fcmd cmd' = None
    and fxor xor' = None
    and fell ast' = None
    and freq ast' = None
    and fsqb ast' = None
    and fano (  ) = None
    and fdsh (  ) = None
    and fssh (  ) = None
    and fseq seq' = None
    and feps (  ) = None
//      let e = ref None in
//      let pred ast' = match eval ast' with
//        | None -> false
//        | err  -> e := err; true
//      in if List.exists pred seq'
//      then !e
//      else None

    member __.Parse(argv':string array, args':Arguments.Dictionary) =
      i := 0;
      len := argv'.Length;
      argv := argv';
      args := args';
      match eval ast with
        | _ when !i < !len -> ArgvException("Illegal parameter: " + argv'.[!i])
                              |> raise
        | None             -> args'
        | Some(err)        -> let pos = FParsec.Position("", 0L, 0L, 0L) in
                              Err.ParserError(pos, null, err).ToString()
                              |> ArgvException
                              |> raise
    member __.Ast = ast
  end
;;
