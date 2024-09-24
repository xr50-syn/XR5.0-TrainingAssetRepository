﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using XR5_0TrainingRepo.Models;

namespace XR5_0TrainingRepo.Controllers
{
    [Route("/xr50/training-repo/xr50app-management/[controller]")]
    [ApiController]
    public class XR50AppController : ControllerBase
    {
        private readonly XR50AppContext _context;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
     
        public XR50AppController(XR50AppContext context, HttpClient httpClient, IConfiguration configuration)
        {
            _context = context;
            _httpClient = httpClient;
             _configuration= configuration;
        }

        // GET: api/XR50App
        [HttpGet]
        public async Task<ActionResult<IEnumerable<XR50App>>> GetApps()
        {
            return await _context.Apps.ToListAsync();
        }
        
        // GET: api/XR50App/5
        [HttpGet("{appName}")]
        public async Task<ActionResult<XR50App>> GetXR50App(string appName)
        {
            var XR50App = await _context.Apps.FindAsync(appName);

            if (XR50App == null)
            {
                return NotFound();
            }

            return XR50App;
        }

        /*
        // PUT: api/XR50App/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutXR50App(long id, XR50App XR50App)
        {
            if (id != XR50App.AppId)
            {
                return BadRequest();
            }

            _context.Entry(XR50App).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!XR50AppExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }
        */

        // POST: api/XR50App
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<XR50App>> PostXR50App(XR50App XR50App)
        {

            _context.Apps.Add(XR50App);

            await _context.SaveChangesAsync();
            var values = new List<KeyValuePair<string, string>>();
            values.Add(new KeyValuePair<string, string>("groupid", XR50App.OwncloudGroup));
            FormUrlEncodedContent messageContent = new FormUrlEncodedContent(values);
            string username = _configuration.GetValue<string>("OwncloudSettings:Admin");
            string password = _configuration.GetValue<string>("OwncloudSettings:Password");
            string uri_base = _configuration.GetValue<string>("OwncloudSettings:BaseAPI");
            string uri_path = _configuration.GetValue<string>("OwncloudSettings:GroupManagementPath");
            string webdav_base = _configuration.GetValue<string>("OwncloudSettings:BaseWebDAV");
            string authenticationString = $"{username}:{password}";
            var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.ASCII.GetBytes(authenticationString));
            var request = new HttpRequestMessage(HttpMethod.Post, uri_path)
            {
                Content = messageContent
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);
            _httpClient.BaseAddress = new Uri(uri_base);
           // _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Basic {base64EncodedAuthenticationString}");
            var result = _httpClient.SendAsync(request).Result;
            string resultContent = result.Content.ReadAsStringAsync().Result;
            //Console.WriteLine($"Response content: {resultContent}");


            // Create root dir for the App
            string cmd = $"/C curl -X MKCOL -u {username}:{password} --cookie \"XDEBUG_SESSION=MROW4A;path=/;\"  \"{webdav_base}/{XR50App.OwncloudDirectory}/\"";
            Console.WriteLine( cmd );
            
            System.Diagnostics.Process.Start("CMD.exe", cmd) ;
            return CreatedAtAction("PostXR50App", XR50App);
        }

        // DELETE: api/XR50App/5
        [HttpDelete("{appName}")]
        public async Task<IActionResult> DeleteXR50App(string appName)
        {
            var XR50App = await _context.Apps.FindAsync(appName);
            if (XR50App == null)
            {
                Console.WriteLine($"Did not find XR app with id: {appName}");
                return NotFound();
            }

            _context.Apps.Remove(XR50App);
            await _context.SaveChangesAsync();

            var values = new List<KeyValuePair<string, string>>();
            FormUrlEncodedContent messageContent = new FormUrlEncodedContent(values);
            string username = _configuration.GetValue<string>("OwncloudSettings:Admin");
            string password = _configuration.GetValue<string>("OwncloudSettings:Password");
            string uri_base = _configuration.GetValue<string>("OwncloudSettings:BaseAPI");
            string uri_path = _configuration.GetValue<string>("OwncloudSettings:GroupManagementPath");
            string webdav_base = _configuration.GetValue<string>("OwncloudSettings:BaseWebDAV");

            string authenticationString = $"{username}:{password}";
            var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.ASCII.GetBytes(authenticationString));
            var request = new HttpRequestMessage(HttpMethod.Delete, $"{uri_path}/{XR50App.OwncloudGroup}")
            {
                Content = messageContent
            };
            Console.WriteLine(XR50App.OwncloudGroup);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);
            _httpClient.BaseAddress = new Uri(uri_base);
            //_httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Basic {base64EncodedAuthenticationString}");
            var result = _httpClient.SendAsync(request).Result;
            string resultContent = result.Content.ReadAsStringAsync().Result;
            // Delete root dir for the App
            string cmd = $"/C curl -X DELETE -u {username}:{password} --cookie \"XDEBUG_SESSION=MROW4A;path=/;\"  \"{webdav_base}/{XR50App.OwncloudDirectory}/\"";
            Console.WriteLine(cmd);
            System.Diagnostics.Process.Start("CMD.exe", cmd);
            //Console.WriteLine($"Response content: {resultContent}");
            return NoContent();
        }

        private bool XR50AppExists(string appName)
        {
            return _context.Apps.Any(e => e.AppName.Equals(appName));
        }
    }
}