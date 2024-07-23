﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using XR5_0TrainingRepo.Models;

namespace XR5_0TrainingRepo.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class XR50AppController : ControllerBase
    {
        private readonly XR50AppContext _context;
        private readonly HttpClient _httpClient;
        public XR50AppController(XR50AppContext context, HttpClient httpClient)
        {
            _context = context;
            _httpClient = httpClient;
        }

        // GET: api/XR50App
        [HttpGet]
        public async Task<ActionResult<IEnumerable<XR50App>>> GetApps()
        {

            var values = new List<KeyValuePair<string, string>>();
            FormUrlEncodedContent messageContent = new FormUrlEncodedContent(values);
            string username = "emmie";
            string password = "!@m!nL0v3W!th@my";

            string authenticationString = $"{username}:{password}";
            var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.ASCII.GetBytes(authenticationString));

            var request = new HttpRequestMessage(HttpMethod.Get, "")
            {
                Content = messageContent
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);
            _httpClient.BaseAddress = new Uri("http://192.168.169.6:8080/ocs/v1.php/cloud/groups");
            // _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Basic {base64EncodedAuthenticationString}");
            var result = _httpClient.SendAsync(request).Result;
            string resultContent = result.Content.ReadAsStringAsync().Result;
            //Console.WriteLine($"Response content: {resultContent}");
            return await _context.Apps.ToListAsync();
        }
        
        // GET: api/XR50App/5
        [HttpGet("{appName}")]
        public async Task<ActionResult<XR50App>> GetXR50App(string appName)
        {
            var xR50App = await _context.Apps.FindAsync(appName);

            if (xR50App == null)
            {
                return NotFound();
            }

            return xR50App;
        }

        /*
        // PUT: api/XR50App/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutXR50App(long id, XR50App xR50App)
        {
            if (id != xR50App.AppId)
            {
                return BadRequest();
            }

            _context.Entry(xR50App).State = EntityState.Modified;

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
        public async Task<ActionResult<XR50App>> PostXR50App(XR50App xR50App)
        {

            _context.Apps.Add(xR50App);

            await _context.SaveChangesAsync();
            var values = new List<KeyValuePair<string, string>>();
            values.Add(new KeyValuePair<string, string>("groupid", xR50App.OwncloudGroup));
            FormUrlEncodedContent messageContent = new FormUrlEncodedContent(values);
            string username = "emmie";
            string password = "!@m!nL0v3W!th@my";
            string authenticationString = $"{username}:{password}";
            var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.ASCII.GetBytes(authenticationString));
            var request = new HttpRequestMessage(HttpMethod.Post, "")
            {
                Content = messageContent
            };
            _httpClient.BaseAddress = new Uri("http://192.168.169.6:8080/ocs/v1.php/cloud/groups");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Basic {base64EncodedAuthenticationString}");
            var result = _httpClient.SendAsync(request).Result;
            string resultContent = result.Content.ReadAsStringAsync().Result;
            Console.WriteLine($"Response content: {resultContent}");
            return CreatedAtAction("GetXR50App", new { id = xR50App.AppName }, xR50App);
        }

        // DELETE: api/XR50App/5
        [HttpDelete("{appName}")]
        public async Task<IActionResult> DeleteXR50App(string appName)
        {
            var xR50App = await _context.Apps.FindAsync(appName);
            if (xR50App == null)
            {
                Console.WriteLine($"Did not find XR app with id: {appName}");
                return NotFound();
            }

            _context.Apps.Remove(xR50App);
            await _context.SaveChangesAsync();

            var values = new List<KeyValuePair<string, string>>();
            FormUrlEncodedContent messageContent = new FormUrlEncodedContent(values);
            string username = "emmie";
            string password = "!@m!nL0v3W!th@my";

            string authenticationString = $"{username}:{password}";
            var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.ASCII.GetBytes(authenticationString));
            var request = new HttpRequestMessage(HttpMethod.Delete, $"/ocs/v1.php/cloud/groups/{xR50App.OwncloudGroup}")
            {
                Content = messageContent
            };
            Console.WriteLine(xR50App.OwncloudGroup);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);
            _httpClient.BaseAddress = new Uri("http://192.168.169.6:8080");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Basic {base64EncodedAuthenticationString}");
            var result = _httpClient.SendAsync(request).Result;
            string resultContent = result.Content.ReadAsStringAsync().Result;
            //Console.WriteLine($"Response content: {resultContent}");
            return NoContent();
        }

        private bool XR50AppExists(string appName)
        {
            return _context.Apps.Any(e => e.AppName.Equals(appName));
        }
    }
}