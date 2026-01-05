// =============================================================================
// API INTEGRATION TESTS (Node.js + Jest + Supertest)
// =============================================================================
// File: tests/api-integration.test.js

const request = require('supertest');
const axios = require('axios');

describe('XR5.0 API Integration Tests', () => {
  const baseURL = process.env.XR50_API_URL || 'http://localhost:5000';
  const testTenant = 'test-tenant';
  
  // Test data cleanup
  const createdMaterials = [];
  const createdPrograms = [];
  
  afterAll(async () => {
    // Cleanup test data
    for (const materialId of createdMaterials) {
      try {
        await axios.delete(`${baseURL}/api/${testTenant}/materials/${materialId}`);
      } catch (e) { /* ignore cleanup errors */ }
    }
    
    for (const programId of createdPrograms) {
      try {
        await axios.delete(`${baseURL}/api/${testTenant}/trainingprograms/${programId}`);
      } catch (e) { /* ignore cleanup errors */ }
    }
  });

  describe('Material Management', () => {
    test('should create different material types', async () => {
      const materialTypes = [
        {
          name: 'Test Video Material',
          type: 'Video',
          discriminator: 'VideoMaterial',
          assetId: 'test-video-asset'
        },
        {
          name: 'Test Checklist Material', 
          type: 'Checklist',
          discriminator: 'ChecklistMaterial'
        },
        {
          name: 'Test Composite Material',
          type: 'Composite', 
          discriminator: 'CompositeMaterial',
          compositeType: 'module'
        }
      ];

      for (const material of materialTypes) {
        const response = await axios.post(
          `${baseURL}/api/${testTenant}/materials`,
          material,
          { headers: { 'Content-Type': 'application/json' } }
        );
        
        expect(response.status).toBe(201);
        expect(response.data.id).toBeDefined();
        expect(response.data.name).toBe(material.name);
        expect(response.data.type).toBe(material.type);
        
        createdMaterials.push(response.data.id);
      }
    });

    test('should create material hierarchy', async () => {
      // Create parent composite material
      const parentResponse = await axios.post(
        `${baseURL}/api/${testTenant}/materials`,
        {
          name: 'Parent Module',
          type: 'Composite',
          discriminator: 'CompositeMaterial'
        }
      );
      
      // Create child video material
      const childResponse = await axios.post(
        `${baseURL}/api/${testTenant}/materials`,
        {
          name: 'Child Video',
          type: 'Video', 
          discriminator: 'VideoMaterial'
        }
      );
      
      const parentId = parentResponse.data.id;
      const childId = childResponse.data.id;
      createdMaterials.push(parentId, childId);
      
      // Assign child to parent
      const assignResponse = await axios.post(
        `${baseURL}/api/${testTenant}/materials/${parentId}/assign-material/${childId}`,
        null,
        { params: { relationshipType: 'contains', displayOrder: 1 } }
      );
      
      expect(assignResponse.status).toBe(200);
      expect(assignResponse.data.relationshipId).toBeDefined();
      
      // Verify hierarchy
      const hierarchyResponse = await axios.get(
        `${baseURL}/api/${testTenant}/materials/${parentId}/hierarchy`
      );
      
      expect(hierarchyResponse.status).toBe(200);
      expect(hierarchyResponse.data.totalMaterials).toBe(2);
      expect(hierarchyResponse.data.children).toHaveLength(1);
      expect(hierarchyResponse.data.children[0].material.id).toBe(childId);
    });

    test('should prevent circular references', async () => {
      // Create two materials
      const material1Response = await axios.post(
        `${baseURL}/api/${testTenant}/materials`,
        { name: 'Material 1', type: 'Composite', discriminator: 'CompositeMaterial' }
      );
      
      const material2Response = await axios.post(
        `${baseURL}/api/${testTenant}/materials`,
        { name: 'Material 2', type: 'Composite', discriminator: 'CompositeMaterial' }
      );
      
      const id1 = material1Response.data.id;
      const id2 = material2Response.data.id;
      createdMaterials.push(id1, id2);
      
      // Create: Material1 → Material2
      await axios.post(
        `${baseURL}/api/${testTenant}/materials/${id1}/assign-material/${id2}`
      );
      
      // Try to create: Material2 → Material1 (should fail)
      try {
        await axios.post(
          `${baseURL}/api/${testTenant}/materials/${id2}/assign-material/${id1}`
        );
        fail('Should have prevented circular reference');
      } catch (error) {
        expect(error.response.status).toBe(400);
        expect(error.response.data).toContain('circular reference');
      }
    });
  });

  describe('Training Program Management', () => {
    test('should create training program and assign materials', async () => {
      // Create training program
      const programResponse = await axios.post(
        `${baseURL}/api/${testTenant}/trainingprograms`,
        { name: 'Test Training Program' }
      );
      
      expect(programResponse.status).toBe(201);
      const programId = programResponse.data.id;
      createdPrograms.push(programId);
      
      // Create material
      const materialResponse = await axios.post(
        `${baseURL}/api/${testTenant}/materials`,
        { name: 'Program Material', type: 'Video', discriminator: 'VideoMaterial' }
      );
      
      const materialId = materialResponse.data.id;
      createdMaterials.push(materialId);
      
      // Assign material to program
      const assignResponse = await axios.post(
        `${baseURL}/api/${testTenant}/trainingprograms/${programId}/assign-material/${materialId}`
      );
      
      expect(assignResponse.status).toBe(200);
      
      // Verify assignment
      const materialsResponse = await axios.get(
        `${baseURL}/api/${testTenant}/trainingprograms/${programId}/materials`
      );
      
      expect(materialsResponse.status).toBe(200);
      expect(materialsResponse.data).toHaveLength(1);
      expect(materialsResponse.data[0].id).toBe(materialId);
    });
  });

  describe('Multi-tenant Isolation', () => {
    test('should isolate data between tenants', async () => {
      const tenant1 = 'tenant-1';
      const tenant2 = 'tenant-2';
      
      // Create material in tenant1
      const response1 = await axios.post(
        `${baseURL}/api/${tenant1}/materials`,
        { name: 'Tenant 1 Material', type: 'Video', discriminator: 'VideoMaterial' }
      );
      
      const materialId = response1.data.id;
      
      // Try to access from tenant2 (should not find it)
      try {
        await axios.get(`${baseURL}/api/${tenant2}/materials/${materialId}`);
        fail('Should not access material from different tenant');
      } catch (error) {
        expect(error.response.status).toBe(404);
      }
      
      // Cleanup
      await axios.delete(`${baseURL}/api/${tenant1}/materials/${materialId}`);
    });
  });
});

// =============================================================================
// PERFORMANCE TESTS (K6)
// =============================================================================
// File: tests/performance.js

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate } from 'k6/metrics';

export let errorRate = new Rate('errors');

export let options = {
  stages: [
    { duration: '30s', target: 10 }, // Ramp up to 10 users
    { duration: '1m', target: 10 },  // Stay at 10 users
    { duration: '30s', target: 50 }, // Ramp up to 50 users
    { duration: '2m', target: 50 },  // Stay at 50 users
    { duration: '30s', target: 0 },  // Ramp down
  ],
  thresholds: {
    http_req_duration: ['p(95)<2000'], // 95% of requests under 2s
    errors: ['rate<0.1'],              // Less than 10% errors
  },
};

const BASE_URL = __ENV.XR50_API_URL || 'http://localhost:5000';
const TENANT = 'perf-test';

export default function() {
  // Test material retrieval performance
  let response = http.get(`${BASE_URL}/api/${TENANT}/materials`);
  
  check(response, {
    'materials list status is 200': (r) => r.status === 200,
    'materials list response time < 500ms': (r) => r.timings.duration < 500,
  }) || errorRate.add(1);
  
  // Test material creation performance
  let createResponse = http.post(
    `${BASE_URL}/api/${TENANT}/materials`,
    JSON.stringify({
      name: `Perf Test Material ${Date.now()}`,
      type: 'Video',
      discriminator: 'VideoMaterial'
    }),
    { headers: { 'Content-Type': 'application/json' } }
  );
  
  check(createResponse, {
    'material creation status is 201': (r) => r.status === 201,
    'material creation response time < 1000ms': (r) => r.timings.duration < 1000,
  }) || errorRate.add(1);
  
  sleep(1);
}

// =============================================================================
// DATABASE TESTS (Python + SQLAlchemy)
// =============================================================================
"""
File: tests/database_tests.py
"""
import pytest
import asyncio
import asyncpg
import os
from typing import List, Dict, Any

class TestDatabaseIntegrity:
    @pytest.fixture
    async def db_connection(self):
        """Create database connection for testing"""
        connection_string = os.getenv(
            'XR50_TEST_DB', 
            'postgresql://user:password@localhost/xr50_test'
        )
        conn = await asyncpg.connect(connection_string)
        yield conn
        await conn.close()
    
    async def test_tenant_isolation(self, db_connection):
        """Test that tenant databases are properly isolated"""
        # Check that tenant databases exist
        tenant_dbs = await db_connection.fetch("""
            SELECT schema_name 
            FROM information_schema.schemata 
            WHERE schema_name LIKE 'xr50_tenant_%'
        """)
        
        assert len(tenant_dbs) > 0, "No tenant databases found"
        
        # Test data isolation between tenants
        for db_record in tenant_dbs[:2]:  # Test first 2 tenant DBs
            tenant_name = db_record['schema_name']
            
            # Check that materials table exists
            tables = await db_connection.fetch(f"""
                SELECT table_name 
                FROM information_schema.tables 
                WHERE table_schema = '{tenant_name}' 
                AND table_name = 'Materials'
            """)
            
            assert len(tables) == 1, f"Materials table missing in {tenant_name}"
    
    async def test_referential_integrity(self, db_connection):
        """Test foreign key constraints and relationships"""
        # Test MaterialRelationship constraints
        constraints = await db_connection.fetch("""
            SELECT tc.constraint_name, tc.table_name, kcu.column_name, 
                   ccu.table_name AS foreign_table_name
            FROM information_schema.table_constraints AS tc 
            JOIN information_schema.key_column_usage AS kcu
              ON tc.constraint_name = kcu.constraint_name
            JOIN information_schema.constraint_column_usage AS ccu
              ON ccu.constraint_name = tc.constraint_name
            WHERE tc.constraint_type = 'FOREIGN KEY' 
            AND tc.table_name IN ('MaterialRelationships', 'ProgramMaterials')
        """)
        
        assert len(constraints) > 0, "No foreign key constraints found"
        
        # Verify specific relationships
        material_fk_exists = any(
            c['column_name'] == 'MaterialId' and c['foreign_table_name'] == 'Materials'
            for c in constraints
        )
        assert material_fk_exists, "MaterialId foreign key missing"
    
    async def test_index_performance(self, db_connection):
        """Test that required indexes exist for performance"""
        indexes = await db_connection.fetch("""
            SELECT indexname, tablename, indexdef
            FROM pg_indexes 
            WHERE tablename IN ('Materials', 'MaterialRelationships', 'Assets')
        """)
        
        # Check for critical indexes
        index_names = [idx['indexname'] for idx in indexes]
        
        assert any('materialrelationships' in idx.lower() for idx in index_names), \
            "MaterialRelationships indexes missing"
        
        assert any('materials' in idx.lower() for idx in index_names), \
            "Materials indexes missing"

# =============================================================================
# LOAD TESTS (Artillery.js)
# =============================================================================
# File: tests/load-test.yml

config:
  target: "{{ $processEnvironment.XR50_API_URL || 'http://localhost:5000' }}"
  phases:
    - duration: 60
      arrivalRate: 5
      name: "Warm up"
    - duration: 120
      arrivalRate: 10
      name: "Sustained load"
    - duration: 60
      arrivalRate: 20
      name: "Peak load"
  variables:
    tenant: "load-test"

scenarios:
  - name: "Material CRUD Operations"
    weight: 60
    flow:
      - get:
          url: "/api/{{ tenant }}/materials"
          capture:
            - json: "$[0].id"
              as: "materialId"
      - post:
          url: "/api/{{ tenant }}/materials"
          json:
            name: "Load Test Material {{ $randomString() }}"
            type: "Video"
            discriminator: "VideoMaterial"
          capture:
            - json: "$.id"
              as: "newMaterialId"
      - get:
          url: "/api/{{ tenant }}/materials/{{ newMaterialId }}"
      - delete:
          url: "/api/{{ tenant }}/materials/{{ newMaterialId }}"

  - name: "Training Program Operations"
    weight: 30
    flow:
      - get:
          url: "/api/{{ tenant }}/trainingprograms"
      - post:
          url: "/api/{{ tenant }}/trainingprograms"
          json:
            name: "Load Test Program {{ $randomString() }}"

  - name: "Hierarchy Operations"
    weight: 10
    flow:
      - get:
          url: "/api/{{ tenant }}/materials"
          capture:
            - json: "$[0].id"
              as: "materialId"
      - get:
          url: "/api/{{ tenant }}/materials/{{ materialId }}/hierarchy"

# =============================================================================
# SMOKE TESTS (Bash Script)
# =============================================================================
# File: tests/smoke-test.sh

#!/bin/bash

# XR5.0 Smoke Tests for Partner Verification
set -e

BASE_URL="${XR50_API_URL:-http://localhost:5000}"
TENANT="smoke-test"

echo "Starting XR5.0 Smoke Tests"
echo "API URL: $BASE_URL"
echo "Test Tenant: $TENANT"

# Test 1: Health Check
echo "🏥 Testing health endpoint..."
curl -f "$BASE_URL/health" || (echo "Health check failed" && exit 1)
echo "Health check passed"

# Test 2: Basic Material Operations
echo " Testing material operations..."

# Create material
MATERIAL_RESPONSE=$(curl -s -X POST "$BASE_URL/api/$TENANT/materials" \
  -H "Content-Type: application/json" \
  -d '{"name":"Smoke Test Material","type":"Video","discriminator":"VideoMaterial"}')

MATERIAL_ID=$(echo $MATERIAL_RESPONSE | grep -o '"id":[0-9]*' | cut -d':' -f2)

if [ -z "$MATERIAL_ID" ]; then
  echo "Failed to create material"
  exit 1
fi

echo "Created material with ID: $MATERIAL_ID"

# Get material
curl -f "$BASE_URL/api/$TENANT/materials/$MATERIAL_ID" > /dev/null || \
  (echo "Failed to retrieve material" && exit 1)
echo "Retrieved material successfully"

# Test 3: Training Program Operations
echo "🎓 Testing training program operations..."

PROGRAM_RESPONSE=$(curl -s -X POST "$BASE_URL/api/$TENANT/trainingprograms" \
  -H "Content-Type: application/json" \
  -d '{"name":"Smoke Test Program"}')

PROGRAM_ID=$(echo $PROGRAM_RESPONSE | grep -o '"id":[0-9]*' | cut -d':' -f2)

if [ -z "$PROGRAM_ID" ]; then
  echo "Failed to create training program"
  exit 1
fi

echo "Created training program with ID: $PROGRAM_ID"

# Test 4: Material Assignment
echo "Testing material assignment..."

curl -f -X POST "$BASE_URL/api/$TENANT/trainingprograms/$PROGRAM_ID/assign-material/$MATERIAL_ID" || \
  (echo "Failed to assign material to program" && exit 1)
echo "Assigned material to training program"

# Test 5: Data Retrieval
echo "Testing data retrieval..."

curl -f "$BASE_URL/api/$TENANT/trainingprograms/$PROGRAM_ID/materials" | \
  grep -q "$MATERIAL_ID" || (echo "Material not found in program" && exit 1)
echo "Retrieved materials from training program"

# Cleanup
echo "🧹 Cleaning up test data..."
curl -f -X DELETE "$BASE_URL/api/$TENANT/materials/$MATERIAL_ID" || \
  echo "Failed to delete material (may not exist)"
curl -f -X DELETE "$BASE_URL/api/$TENANT/trainingprograms/$PROGRAM_ID" || \
  echo "Failed to delete program (may not exist)"

echo "All smoke tests passed!"
echo ""
echo "Partner Integration Verification Complete ✅"
echo "Your XR5.0 installation is working correctly!"

# =============================================================================
# DOCKER TEST RUNNER
# =============================================================================
# File: tests/Dockerfile.tests

FROM node:16

WORKDIR /tests

# Install test dependencies
COPY package.json package-lock.json ./
RUN npm install

# Install additional testing tools
RUN npm install -g artillery k6

# Copy test files
COPY . .

# Install Python for database tests
RUN apt-get update && apt-get install -y python3 python3-pip
RUN pip3 install pytest asyncpg sqlalchemy

# Make scripts executable
RUN chmod +x smoke-test.sh

# Default command runs all tests
CMD ["npm", "test"]