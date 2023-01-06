// Copyright 2018 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using cartservice.interfaces;
using Google.Protobuf;
using Grpc.Core;
using OpenTracing;
using OpenTracing.Util;
using StackExchange.Redis;

namespace cartservice.cartstore
{
  public class RedisCartStore : ICartStore
  {

   
    private const string CART_FIELD_NAME = "cart";

    private volatile ConnectionMultiplexer redis;
    private readonly object locker = new object();
    private readonly byte[] emptyCartBytes;

    private static double EXTERNAL_DB_ACCESS_RATE = Convert.ToDouble(Environment.GetEnvironmentVariable("EXTERNAL_DB_ACCESS_RATE"));
    private static Int16 EXTERNAL_DB_MAX_DURATION_MILLIS = Convert.ToInt16(Environment.GetEnvironmentVariable("EXTERNAL_DB_MAX_DURATION_MILLIS"));
    private static double EXTERNAL_DB_ERROR_RATE = Convert.ToDouble(Environment.GetEnvironmentVariable("EXTERNAL_DB_ERROR_RATE"));

    public static string EXTERNAL_DB_NAME = Environment.GetEnvironmentVariable("EXTERNAL_DB_NAME") ?? "global.datastore";

    public static string CACHE_HIT_REDIS_ERROR = Environment.GetEnvironmentVariable("CACHE_HIT_REDIS_ERROR") ?? "true";
    public static string SCAN_REDIS_ERROR = Environment.GetEnvironmentVariable("SCAN_REDIS_ERROR") ?? "true";
    private static Int16 FORCED_REDIS_SCAN_COUNT = Convert.ToInt16(Environment.GetEnvironmentVariable("FORCED_REDIS_SCAN_COUNT"));

     public static string REDIS_ADDR = Environment.GetEnvironmentVariable("REDIS_ADDR") ?? "False";
   
    private readonly ITracer _tracer;
    private readonly Random _random;

    public RedisCartStore(ConnectionMultiplexer connection)
    {
      redis = connection;
      // Serialize empty cart into byte array.
      var cart = new Hipstershop.Cart();
      emptyCartBytes = cart.ToByteArray();

      _tracer = GlobalTracer.Instance;
      _random = new Random();

    }

    public Task InitializeAsync()
    {
      return Task.CompletedTask;
    }

    public async Task AddItemAsync(string userId, string productId, int quantity)
    {
      Console.WriteLine($"AddItemAsync called with userId={userId}, productId={productId}, quantity={quantity}");

      try
      {
        var db = redis.GetDatabase();

        // Access the cart from the cache
        var value = await db.HashGetAsync(userId, CART_FIELD_NAME);

        Hipstershop.Cart cart;
        if (value.IsNull)
        {
          cart = new Hipstershop.Cart();
          cart.UserId = userId;
          cart.Items.Add(new Hipstershop.CartItem { ProductId = productId, Quantity = quantity });
        }
        else
        {
          cart = Hipstershop.Cart.Parser.ParseFrom(value);
          var existingItem = cart.Items.SingleOrDefault(i => i.ProductId == productId);
          if (existingItem == null)
          {
            cart.Items.Add(new Hipstershop.CartItem { ProductId = productId, Quantity = quantity });
          }
          else
          {
            existingItem.Quantity += quantity;
          }
        }

        await db.HashSetAsync(userId, new[] { new HashEntry(CART_FIELD_NAME, cart.ToByteArray()) });

        // Attempt to access "external database" some percentage of the time
        if (_random.NextDouble() < EXTERNAL_DB_ACCESS_RATE)
        {
          using (IScope scope = _tracer.BuildSpan("Cart.DbQuery.UpdateCart").WithTag("span.kind", "client").StartActive())
          {
            string  db_Statement = "";
            ISpan span = scope.Span;
            db_Statement =  "INSERT INTO 'cart' ('productid', 'quantity', 'currency', 'region', 'order_time', 'customer_json') VALUES ($1, $2, $3, $4, $5, $6) RETURNING 'cartid'";
            span.SetTag("db.system", "mysql");
            span.SetTag("peer.service", "mysql:LxvGChW075");
            span.SetTag(  "db.statement",db_Statement);

            if (_random.NextDouble() < EXTERNAL_DB_ERROR_RATE)
            {
              span.SetTag("error", "true");
            }

            StackExchange(_random.Next(0, EXTERNAL_DB_MAX_DURATION_MILLIS));
          }
        }
      }
      catch (Exception ex)
      {
        throw new RpcException(new Grpc.Core.Status(StatusCode.FailedPrecondition, $"Can't access cart storage. {ex}"));
      }
    }

    public async Task EmptyCartAsync(string userId)
    {
      Console.WriteLine($"EmptyCartAsync called with userId={userId}");

      try
      {
        var db = redis.GetDatabase();

        // Update the cache with empty cart for given user
        await db.HashSetAsync(userId, new[] { new HashEntry(CART_FIELD_NAME, emptyCartBytes) });
        using (IScope scope = _tracer.BuildSpan("Cart.DbQueryEmptyCart").WithTag("span.kind", "client").StartActive())
          {
            string  db_Statement = "";
            ISpan span = scope.Span;
            db_Statement = "DELETE FROM 'cart' WHERE 'cartid' = ($1)";
            span.SetTag("db.system", "mysql");
            span.SetTag("peer.service", "mysql:LxvGChW075");
            span.SetTag(  "db.statement",db_Statement);

            if (_random.NextDouble() < EXTERNAL_DB_ERROR_RATE)
            {
              span.SetTag("error", "true");
            }

            Thread.Sleep(_random.Next(0, EXTERNAL_DB_MAX_DURATION_MILLIS));
          }

      }
      catch (Exception ex)
      {
        throw new RpcException(new Grpc.Core.Status(StatusCode.FailedPrecondition, $"Can't access cart storage. {ex}"));
      }
    }

    public async Task<Hipstershop.Cart> GetCartAsync(string userId)
    {
      Console.WriteLine($"GetCartAsync called with userId={userId}");
      try
      {
        var db = redis.GetDatabase();

        // Access the cart from the cache
        if (CACHE_HIT_REDIS_ERROR == "false" || CACHE_HIT_REDIS_ERROR == "False") // If we pass True forFORCE_REDIS_ERROR we are going to Create extra lopad on the DB
        {
          userId = "";  // Force a Cache Mishit
           Thread.Sleep(6000); // Force Latency 
          Console.WriteLine($"Creating MisHit with {userId}");
        }  
        var value = await db.HashGetAsync(userId, CART_FIELD_NAME);
        if (!value.IsNull)
        {
          // Attempt to access "external database" some percentage of the time. This happens after
          // our redis call to represent some kind fo "cache miss" or secondary call that is not
          // in the redis cache.
          //if (_random.NextDouble() < EXTERNAL_DB_ACCESS_RATE)
         // {
          using (IScope scope = _tracer.BuildSpan("Cart.DbQuery.GetCart").WithTag("span.kind", "client").StartActive())
          {
            string  db_Statement = "";
            ISpan span = scope.Span;
            db_Statement = "SELECT 'cart'.* FROM 'cart' WHERE 'cartid' = ($1)";
    
            span.SetTag("db.system", "mysql");
            span.SetTag("peer.service", "mysql:LxvGChW075");
            span.SetTag( "db.statement",db_Statement);

            if (_random.NextDouble() < EXTERNAL_DB_ERROR_RATE)
            {
              span.SetTag("error", "true");
            }

            Thread.Sleep(_random.Next(0, EXTERNAL_DB_MAX_DURATION_MILLIS));
             // }
           }
          return Hipstershop.Cart.Parser.ParseFrom(value);
        }

          // We decided to return empty cart in cases when user wasn't in the cache before
          return new Hipstershop.Cart();
        }
        catch (Exception ex)
        {
          throw new RpcException(new Grpc.Core.Status(StatusCode.FailedPrecondition, $"Can't access cart storage. {ex}"));
        }  
    }

    public bool Ping()
    {
        try
        {
          var cache = redis.GetDatabase();
          var res = cache.Ping();
          
          if (SCAN_REDIS_ERROR == "false" || SCAN_REDIS_ERROR == "False") // If we pass True forFORCE_REDIS_ERROR we are going to Create extra lopad on the DB
          {
            Console.WriteLine($"going to create extra load on redis server");
            for (int i=0; i <= MyGlobals.Total; i++)
            {
              //redis.GetServer(REDIS_ADDR).Keys(); 
              var server = redis.GetServer(REDIS_ADDR); 
                    
              foreach (var key in server.Keys())
              {
                Console.WriteLine(key);
              } 
              
          
            } 
            Console.WriteLine($"Ping called with i={MyGlobals.Total}");
            //Thread.Sleep(_random.Next(0, EXTERNAL_DB_MAX_DURATION_MILLIS));
            Thread.Sleep(1000);
            
            if (FORCED_REDIS_SCAN_COUNT <= 0)  
            {
              FORCED_REDIS_SCAN_COUNT = 150;
            } 
            if (MyGlobals.Total <= FORCED_REDIS_SCAN_COUNT) 
            {
              MyGlobals.Total = MyGlobals.Total + 1; 
            }
            else
            {
              MyGlobals.Total = 1;
            }   
          }
          var values = new NameValueEntry[]
          {
              new NameValueEntry("sensor_id", "56"),
              new NameValueEntry("temp", "2")
          };


          var messageId = cache.StreamAdd("sensor_stream", values);

          var streams = cache.StreamRead(new StreamPosition[]
          {
              new StreamPosition("events_stream", "0-0"),
              new StreamPosition("score_stream", "0-0")
          });
          string someKey = cache.KeyRandom();
          using (IScope scope = _tracer.BuildSpan("Cart.DbQuery.ping").WithTag("span.kind", "client").StartActive())
          {
            string  db_Statement = "";
            ISpan span = scope.Span;
            db_Statement = "SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = ($1)";
            span.SetTag("db.system", "mysql"); 
            span.SetTag( "db.statement",db_Statement);
            span.SetTag("peer.service", "mysql:LxvGChW075"); 

            Console.WriteLine($"local dababase using statement={db_Statement}" );
            
          }     
          return res != TimeSpan.Zero;
        }
        catch (Exception)
        {
          return false;
        }
    }
  }

  public static class MyGlobals 
  {
        public static int Total = 1; // can change because not const
  }
}
  
