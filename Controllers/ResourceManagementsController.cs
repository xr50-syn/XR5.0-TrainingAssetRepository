﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using XR5_0TrainingRepo.Models;

namespace XR5_0TrainingRepo.Controllers
{
    [Route("/xr50/training-repo/resource-management/[controller]")]
    [ApiController]
    public class ResourceManagementController : ControllerBase
    {
        private readonly ResourceContext _context;
        private readonly XR50AppContext _xr50AppContext;
        private readonly TrainingContext _xr50TrainingContext;
        private readonly HttpClient _httpClient;
        public ResourceManagementController(ResourceContext context, XR50AppContext xr50AppContext, TrainingContext xr50TrainingContext, HttpClient httpClient)
        {
            _context = context;
            _xr50AppContext = xr50AppContext;
            _xr50TrainingContext = xr50TrainingContext; 
            _httpClient = httpClient;
        }

        // GET: api/ResourceManagements
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ResourceManagement>>> GetResource()
        {
            return await _context.Resource.ToListAsync();
        }

        // GET: api/ResourceManagements/5
        [HttpGet("{ResourceName}")]
        public async Task<ActionResult<ResourceManagement>> GetResourceManagement(string ResourceName)
        {
            var resourceManagement = await _context.Resource.FindAsync(ResourceName);

            if (resourceManagement == null)
            {
                return NotFound();
            }

            return resourceManagement;
        }

        // PUT: api/ResourceManagements/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{ResourceName}")]
        public async Task<IActionResult> PutResourceManagement(string ResourceName, ResourceManagement resourceManagement)
        {
            if (!ResourceName.Equals(resourceManagement.ResourceName))
            {
                return BadRequest();
            }

            _context.Entry(resourceManagement).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ResourceManagementExists(ResourceName))
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

        // POST: api/ResourceManagements
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<ResourceManagement>> PostResourceManagement(ResourceManagement resourceManagement)
        {
            _context.Resource.Add(resourceManagement);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetResourceManagement", new {resourceManagement.ResourceName }, resourceManagement);
        }

        // DELETE: api/ResourceManagements/5
        [HttpDelete("{ResourceName}")]
        public async Task<IActionResult> DeleteResourceManagement(string ResourceName)
        {
            var resourceManagement = await _context.Resource.FindAsync(ResourceName);
            if (resourceManagement == null)
            {
                return NotFound();
            }

            _context.Resource.Remove(resourceManagement);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool ResourceManagementExists(string ResourceName)
        {
            return _context.Resource.Any(e => e.ResourceName.Equals(ResourceName));
        }
    }
}
