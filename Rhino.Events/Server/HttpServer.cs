﻿using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rhino.Events.Storage;

namespace Rhino.Events.Server
{
	public class HttpServer : IDisposable
	{
		private readonly PersistedEvents data;
		private readonly HttpListener httpListener;

		public HttpServer()
		{
			data = new PersistedEvents(new FileStreamSource(), "Data");

			httpListener = new HttpListener
				{
					Prefixes = {"http://+:8080/"},
					IgnoreWriteExceptions = true,
				};
			httpListener.Start();

			for (int i = 0; i < 30; i++)
			{
				ListenAsync();
			}
		}

		private async void ListenAsync()
		{
			var ctx = await Task.Factory.FromAsync<HttpListenerContext>(httpListener.BeginGetContext, httpListener.EndGetContext, null);
			try
			{
				ListenAsync();

			}
			catch (ObjectDisposedException)
			{
				// disposed, exiting
			}

			//Console.WriteLine(ctx.Request.HttpMethod + " " + ctx.Request.RawUrl);
			var streamWriter = new StreamWriter(ctx.Response.OutputStream);
			try
			{
				var id = ctx.Request.QueryString["id"];
				switch (ctx.Request.HttpMethod)
				{
					case "PUT":
						var value = (JObject)JToken.ReadFrom(new JsonTextReader(new StreamReader(ctx.Request.InputStream)));
						if(string.IsNullOrWhiteSpace(id))
							throw new ArgumentException("id query string must have a value");

						var metadata = value.Value<JObject>("@metadata") ?? new JObject();
						value.Remove("@metadata");

						await data.EnqueueAsync(id, metadata, value);

						break;
					case "GET":
						if(string.IsNullOrWhiteSpace(id))
							throw new ArgumentException("id query string must have a value");

						var stream = data.Read(id);
						if (stream == null)
						{
							ctx.Response.StatusCode = 404;
							ctx.Response.StatusDescription = "Not Found";
							return;
						}
						var writer = new JsonTextWriter(streamWriter);
						writer.WriteStartObject();
						writer.WritePropertyName("Stream");
						writer.WriteStartArray();


						foreach (var item in stream)
						{
							writer.WriteStartObject();
							
							writer.WritePropertyName("@metadata");
							writer.WriteStartObject();
							item.Metadata.WriteTo(writer);
							writer.WriteEndObject();

							item.Data.WriteTo(writer);
							writer.WriteEndObject();
						}

						writer.WriteEndArray();
						writer.WriteEndObject();

						break;

					default:
						throw new NotSupportedException("Http Method: " + ctx.Request.HttpMethod);
				}
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				ctx.Response.StatusCode = 500;
				using(var wr = streamWriter)
				{
					wr.Write(e);
				}
			}
			finally
			{
				streamWriter.Flush();
				ctx.Response.Close();
			}
		}

		public void Dispose()
		{
			data.Dispose();
		}
	}
}