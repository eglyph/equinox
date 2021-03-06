﻿module Domain.ContactPreferences

type Id = Id of email: string
let streamName (Id email) = FsCodec.StreamName.create "ContactPreferences" email // TODO hash >> base64

// NOTE - these types and the union case names reflect the actual storage formats and hence need to be versioned with care
module Events =

    type Preferences = { manyPromotions : bool; littlePromotions : bool; productReview : bool; quickSurveys : bool }
    type Value = { email : string; preferences : Preferences }

    type Event =
        | [<System.Runtime.Serialization.DataMember(Name = "contactPreferencesChanged")>]Updated of Value
        interface TypeShape.UnionContract.IUnionContract
    let codec = FsCodec.NewtonsoftJson.Codec.Create<Event>()

module Fold =

    type State = Events.Preferences

    let initial : State = { manyPromotions = false; littlePromotions = false; productReview = false; quickSurveys = false }
    let private evolve _ignoreState = function
        | Events.Updated { preferences = value } -> value
    let fold (state: State) (events: seq<Events.Event>) : State =
        Seq.tryLast events |> Option.fold evolve state
    let isOrigin _ = true
    /// When using AccessStrategy.Custom, we use the (single) event become an unfold, but the logic remains identical
    let transmute events _state =
        [],events

type Command =
    | Update of Events.Value

let interpret command (state : Fold.State) =
    match command with
    | Update ({ preferences = preferences } as value) ->
        if state = preferences then [] else
        [ Events.Updated value ]

type Service internal (resolve : Id -> Equinox.Stream<Events.Event, Fold.State>) =

    let update email value : Async<unit> =
        let stream = resolve email
        let command = let (Id email) = email in Update { email = email; preferences = value }
        stream.Transact(interpret command)

    member __.Update(email, value) =
        update email value

    member __.Read(email) =
        let stream = resolve email
        stream.Query id

let create log resolve =
    let resolve id = Equinox.Stream(log, resolve (streamName id), maxAttempts  = 3)
    Service(resolve)
