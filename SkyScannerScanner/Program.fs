open FSharp.Data
open Types
open Search
open System
open SkyScannerApi

[<EntryPoint>]
let main argv = 

    let request = { 
        FlightDatesToSearchBetween = Search.GetIdealFlightDates(new DateTime(2015, 1, 20), new DateTime(2015, 3, 1)) |> List.ofSeq
        MaxFlightTime = new TimeSpan(22, 0, 0)
        MaxStops = 2
        MaxPrice = 600m
        Destinations = ["LIM"]
    }

    //http://blog.mongodb.org/post/59584347005/enhancing-the-f-developer-experience-with-mongodb
    //& http://www.slideshare.net/mongodb/mongo-db-f-driver-external
//
//    let apiRequest =  {
//        OutboundDate = new DateTime(2010, 1, 1)
//        InboundDate = new DateTime(2010, 1, 30)
//        Destination = "LDN"
//    }
//
//    let doc1 = 
//        MongoCaching.GetSearchResult apiRequest
//
//
//    let results = [
//        {
//            Destination = "LDN"
//            Inbound = {
//                Duration = TimeSpan.FromHours(5.0)
//                NumberOfStops = 1
//                DepartsAt = new DateTime(2010, 1, 30)
//            }
//            Outbound = {
//                Duration = TimeSpan.FromHours(6.0)
//                NumberOfStops = 1
//                DepartsAt = new DateTime(2010, 1, 1)
//            }
//            Price = 50.0m
//        }
//        {
//            Destination = "LDN"
//            Inbound = {
//                Duration = TimeSpan.FromHours(77.0)
//                NumberOfStops = 22
//                DepartsAt = new DateTime(2010, 1, 29)
//            }
//            Outbound = {
//                Duration = TimeSpan.FromHours(11.0)
//                NumberOfStops = 11
//                DepartsAt = new DateTime(2010, 1, 10)
//            }
//            Price = 5440.0m
//        }
//    ]
//
//    MongoCaching.StoreSearchResult apiRequest (Some results)
//
//    let doc = 
//        MongoCaching.GetSearchResult apiRequest

    let result = Search.FindSuitableFlights request |> Async.RunSynchronously
    printfn "Ordered by cost: %A" (result |> List.sortBy (fun x -> x.Price) |> Seq.take 3)
    printfn "Ordered by total flight time: %A" (result |> List.sortBy (fun x -> x.Inbound.Duration + x.Outbound.Duration) |> Seq.take 3)

    Console.ReadLine()
        |> ignore
    0 // return an integer exit code
