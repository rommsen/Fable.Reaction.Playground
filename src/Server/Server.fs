module Server

open System
open System.IO
open System.Threading.Tasks

open Microsoft.AspNetCore
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection

open FSharp.Control.Tasks.V2
open Giraffe
open Reaction
open Reaction.AspNetCore.Middleware
open Shared

open Giraffe.Serialization

let publicPath = Path.GetFullPath "../Client/public"
// let publicPath = Path.GetFullPath "C:\src\FableReactionPlayground\src\Client\public"
let port = 8085us


type LetterMsg =
  | Set of string
  | Get of AsyncReplyChannel<string>

let mailbox =
  MailboxProcessor.Start(fun inbox ->
    let rec loop letterString =
      async {
        let! msg = inbox.Receive()

        match msg with
        | Set letterString ->
            return! loop letterString

        | Get reply ->
            reply.Reply letterString

            return! loop letterString
      }

    loop "Magic Released!"
  )

let getInitLetterString () : Task<string> =
  Get
  |> mailbox.PostAndAsyncReply
  |> Async.StartAsTask

let webApp =
  route "/api/init" >=>
    fun next ctx ->
      task {
        let! letterString = getInitLetterString()
        return! Successful.OK letterString next ctx
      }


let query (connectionId: ConnectionId) (msgs: IAsyncObservable<Msg*ConnectionId>) : IAsyncObservable<Msg*ConnectionId> =
  msgs
  |> AsyncRx.flatMap(fun (msg,id) ->
      match msg with
       | Msg.LetterStringChanged letterString ->
          mailbox.Post (Set letterString)

      | _ -> ()

      AsyncRx.single (msg,id))


let configureApp (app : IApplicationBuilder) =
    app.UseWebSockets()
       .UseReaction<Msg>(fun options ->
       { options with
           Query = query
           Encode = Msg.Encode
           Decode = Msg.Decode
       })
       .UseDefaultFiles()
       .UseStaticFiles()
       .UseGiraffe webApp

let configureServices (services : IServiceCollection) =
    services.AddGiraffe() |> ignore
    let fableJsonSettings = Newtonsoft.Json.JsonSerializerSettings()
    fableJsonSettings.Converters.Add(Fable.JsonConverter())
    services.AddSingleton<IJsonSerializer>(NewtonsoftJsonSerializer fableJsonSettings) |> ignore

WebHost
    .CreateDefaultBuilder()
    .UseWebRoot(publicPath)
    .UseContentRoot(publicPath)
    .Configure(Action<IApplicationBuilder> configureApp)
    .ConfigureServices(configureServices)
    .UseUrls("http://0.0.0.0:" + port.ToString() + "/")
    .Build()
    .Run()