# Voice Material & Asset Processing

## Voice Material

### Fields
- **id**
- **name**
- **description**
- **service_job_id**
- **status**: `enum('ready', 'process', 'notready')`
- **assets**: `[asset_id]` (ex: `[1, 3]`)

---

## Asset

### Fields
- **id**
- **type**
- **name**
- **src**
- **ai_available**: `enum('ready', 'process', 'notready')`
- **job_id**: `string | null`

---

## Create Voice Material

### Endpoint
POST /api/materials

### Backend Flow
1. Call Chatbot API
   POST {ChatbotApi}/document
2. Change assets status to `process`
3. Create Voice Material with status `process`

---

## Get Voice Material

### Endpoint
GET /api/materials/{id}

### Backend Logic
1. Check if all asset IDs listed in `assets` are `ready`
2. If all assets are ready:
   - Change Voice Material status to `ready`
3. Return Voice Material response

---

## Asset Status Synchronization

### Endpoint
GET /api/assets

### Backend Logic
1. For each asset with `ai_available = 'process'`
2. Call Chatbot API
   GET {ChatbotApi}/document/jobs/{job_id}
3. If job status is `success`:
   - Update asset `ai_available` to `ready`

---

## AI Extraction – Asset Management

### Display Assets
GET /api/test-company/assets

### Example Response
```json
[
  {
    "id": "6",
    "type": "video",
    "filetype": "mp4",
    "filename": "67ad9f8738700-3678380-hd_1920_1080_30fps.mp4",
    "ai_available": "notready"
  },
  {
    "id": "8",
    "type": "unity",
    "filetype": "glb",
    "filename": "c6913d09-1750-4b7e-9a61-0a39bfde9086",
    "ai_available": "ready"
  }
]
```

---

## Submit Asset for AI Processing

### Endpoint
POST /asset/{id}/submit
```json
{
 id: 6,
 ai_available: 'ready'
}
```

### Backend Flow
1. POST {ChatbotApi}/document
2. Update asset status to `process`