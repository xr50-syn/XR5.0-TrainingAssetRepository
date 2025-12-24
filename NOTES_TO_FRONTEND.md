# Frontend API Notes

---

## CRITICAL: Request Body Structures

There are different request body structures depending on endpoint and content type:

### Structure 1: JSON Body (PUT and POST without file)

Send the material object **directly** in the body:

```
Content-Type: application/json

{
  "id": "3",
  "name": "Material Name",
  "description": "Description",
  "type": "checklist",
  ...
}
```

### Structure 2: Multipart Form Data (POST with file upload)

Send as **form fields**, not JSON:

```
Content-Type: multipart/form-data

Form Fields:
├── material: '{"name":"Image Name","type":"image",...}'  (JSON string)
├── file: <binary file data>                               (optional)
└── assetData: '{"assetType":"image",...}'                 (JSON string, optional)
```

**Important**: The `material` field value is a **JSON string**, not a nested object.

### WARNING

**DO NOT send `{"material": {...}}` as JSON to PUT or POST!**

If you wrap the material in a JSON object like this:
```json
{
  "material": {
    "id": "3",
    "name": "Material Name"
  }
}
```

The API will not find any properties at the root level. This causes:
- All fields to be parsed as `null`
- The material gets updated with null/empty values
- **Data loss!**

### Summary Table

| Endpoint | Content-Type | Structure |
|----------|--------------|-----------|
| `PUT /materials/{id}` (no file) | `application/json` | Material object directly in body |
| `PUT /materials/{id}` (with file) | `multipart/form-data` | Form fields: `material`, `file`, `assetData` |
| `POST /materials` (no file) | `application/json` | Material object directly in body |
| `POST /materials` (with file) | `multipart/form-data` | Form fields: `material`, `file`, `assetData` |

### File Upload Behavior on PUT

When updating a material with a file:
- If the material already has an asset, the **old asset is deleted** and replaced with the new one
- If the material has no asset, a new asset is created and linked to the material
- Asset-supporting material types: `video`, `image`, `pdf`, `unity`, `default`

---

## PUT /api/{tenantName}/materials/{id}

Updates an existing material. The material object should be sent **directly** in the request body (not wrapped).

### Common Properties (All Types)

```json
{
  "id": "3",
  "name": "Material Name",
  "description": "Material description",
  "type": "checklist",
  "unique_id": "12345",
  "related": [
    {
      "id": "1",
      "name": "Related Material Name"
    }
  ]
}
```

| Property | Type | Required | Notes |
|----------|------|----------|-------|
| id | string/int | Yes | Must match route parameter |
| name | string | No | |
| description | string | No | |
| type | string | Yes | Material type (see below) |
| unique_id | string/int | No | External reference ID |
| related | array | No | Related materials at top level |

### Response

**Success (200)**:
```json
{
  "status": "success",
  "message": "Material 'Material Name' with ID 3 updated successfully"
}
```

**Not Found (404)**:
```
Material with ID 3 not found
```

**ID Mismatch (400)**:
```
ID mismatch: route=3, body=5
```

---

## Material Type: Checklist

```json
{
  "id": "3",
  "name": "Safety Checklist",
  "description": "Pre-flight safety checks",
  "type": "checklist",
  "config": {
    "entries": [
      {
        "id": "7",
        "text": "Check fuel levels",
        "description": "Verify fuel is above minimum",
        "related": [
          {
            "id": "1",
            "name": "Fuel Guide Video"
          }
        ]
      },
      {
        "id": "8",
        "text": "Inspect landing gear",
        "description": "Visual inspection required",
        "related": []
      }
    ]
  },
  "related": [
    {
      "id": "10",
      "name": "Safety Manual PDF"
    }
  ]
}
```

### Entry Properties

| Property | Type | Notes |
|----------|------|-------|
| id | string/int | Entry ID (required for existing entries) |
| text | string | Entry title/label |
| description | string | Entry description |
| related | array | Related materials for this entry |

---

## Material Type: Workflow

```json
{
  "id": "5",
  "name": "Assembly Workflow",
  "description": "Step-by-step assembly process",
  "type": "workflow",
  "config": {
    "steps": [
      {
        "id": "12",
        "stepNumber": 1,
        "title": "Prepare workspace",
        "description": "Clear the work area",
        "instructions": "Remove all unnecessary items",
        "related": [
          {
            "id": "20",
            "name": "Workspace Setup Image"
          }
        ]
      },
      {
        "id": "13",
        "stepNumber": 2,
        "title": "Gather materials",
        "description": "Collect required parts",
        "instructions": "See parts list",
        "related": []
      }
    ]
  }
}
```

### Step Properties

| Property | Type | Notes |
|----------|------|-------|
| id | string/int | Step ID (required for existing steps) |
| stepNumber | int | Order of the step |
| title | string | Step title |
| description | string | Step description |
| instructions | string | Detailed instructions |
| related | array | Related materials for this step |

---

## Material Type: Quiz

```json
{
  "id": "10",
  "name": "Safety Quiz",
  "description": "Test your safety knowledge",
  "type": "quiz",
  "questions": [
    {
      "id": "25",
      "questionNumber": 1,
      "questionType": "choice",
      "text": "What is the first safety step?",
      "description": "Select the correct answer",
      "score": 10,
      "helpText": "Think about preparation",
      "allowMultiple": false,
      "answers": [
        {
          "id": "100",
          "text": "Check equipment",
          "correctAnswer": true,
          "displayOrder": 1,
          "extra": null
        },
        {
          "id": "101",
          "text": "Start immediately",
          "correctAnswer": false,
          "displayOrder": 2,
          "extra": "This is unsafe"
        }
      ],
      "related": [
        {
          "id": "5",
          "name": "Safety Introduction Video"
        }
      ]
    },
    {
      "id": "26",
      "questionNumber": 2,
      "questionType": "boolean",
      "text": "Safety gear is optional",
      "answers": [
        {
          "id": "102",
          "text": "True",
          "correctAnswer": false
        },
        {
          "id": "103",
          "text": "False",
          "correctAnswer": true
        }
      ]
    },
    {
      "id": "27",
      "questionNumber": 3,
      "questionType": "scale",
      "text": "Rate your confidence level",
      "scaleConfig": "{\"startAt\":1,\"size\":5,\"labelMin\":\"Not confident\",\"labelMax\":\"Very confident\",\"isDiscrete\":true}",
      "answers": []
    }
  ]
}
```

### Question Types

| Type | Description | Validation |
|------|-------------|------------|
| text | Free text response | None |
| boolean | True/False question | Must have exactly 2 answers |
| choice | Single selection | Must have at least 2 answers |
| checkboxes | Multiple selection | Must have at least 2 answers |
| scale | Likert/rating scale | Must have scaleConfig |

### Question Type Aliases

The API normalizes these display names to internal types:

| Display Name | Internal Type |
|--------------|---------------|
| True or False, True/False, Yes or No, Yes/No | boolean |
| Multiple choice, Single choice, Radio | choice |
| Selection checkboxes, Checkbox, Multi select | checkboxes |
| Likert, Rating | scale |
| Open, Free text, Open ended | text |

### Question Properties

| Property | Type | Notes |
|----------|------|-------|
| id | string/int | Question ID |
| questionNumber | int | Order of the question |
| questionType | string | See types above |
| text | string | Question text |
| description | string | Additional description |
| score | decimal | Points for correct answer |
| helpText | string | Hint for respondent |
| allowMultiple | bool | Allow multiple answers (for choice) |
| scaleConfig | string | JSON config for scale type |
| answers | array | Answer options |
| related | array | Related materials for this question |

### Answer Properties

| Property | Type | Notes |
|----------|------|-------|
| id | string/int | Answer ID |
| text | string | Answer text |
| correctAnswer | bool | Is this the correct answer |
| displayOrder | int | Order of the answer |
| extra | string | Additional info (feedback, explanation) |

### Scale Configuration (JSON)

```json
{
  "startAt": 1,
  "size": 5,
  "labelMin": "Strongly Disagree",
  "labelMax": "Strongly Agree",
  "isDiscrete": true
}
```

---

## Material Type: Questionnaire

```json
{
  "id": "15",
  "name": "Feedback Form",
  "description": "Post-training feedback",
  "type": "questionnaire",
  "questionnaireType": "feedback",
  "questionnaireConfig": "{}",
  "passingScore": 70.0,
  "config": {
    "entries": [
      {
        "id": "30",
        "questionNumber": 1,
        "questionType": "scale",
        "text": "How would you rate this training?",
        "isRequired": true,
        "scaleConfig": "{\"startAt\":1,\"size\":5}",
        "related": []
      }
    ]
  }
}
```

---

## Material Type: Video

```json
{
  "id": "20",
  "name": "Training Video",
  "description": "Introduction to safety procedures",
  "type": "video",
  "videoPath": "/assets/videos/safety-intro.mp4",
  "videoDuration": 300,
  "videoResolution": "1920x1080",
  "startTime": "00:00:30",
  "annotations": "[{\"time\":10,\"text\":\"Important point\"}]",
  "assetId": 45
}
```

---

## Material Type: Image

```json
{
  "id": "25",
  "name": "Diagram",
  "description": "Assembly diagram",
  "type": "image",
  "imagePath": "/assets/images/diagram.png",
  "imageWidth": 1920,
  "imageHeight": 1080,
  "imageFormat": "png",
  "assetId": 50
}
```

---

## Material Type: PDF

```json
{
  "id": "30",
  "name": "User Manual",
  "description": "Complete user guide",
  "type": "pdf",
  "pdfPath": "/assets/docs/manual.pdf",
  "pdfPageCount": 50,
  "pdfFileSize": 2500000,
  "assetId": 55
}
```

---

## Related Materials

Related materials can be attached at two levels:

### 1. Top-level (on the material itself)

```json
{
  "id": "3",
  "name": "Checklist",
  "type": "checklist",
  "related": [
    { "id": "10", "name": "Supporting Document" }
  ]
}
```

### 2. Subcomponent-level (on entries, steps, questions)

```json
{
  "config": {
    "entries": [
      {
        "id": "7",
        "text": "Entry text",
        "related": [
          { "id": "20", "name": "Entry-specific material" }
        ]
      }
    ]
  }
}
```

Only the `id` is required in the related object. Other properties (name, description) are ignored during update but can be included for readability.

---

## Error Handling

| Status | Cause |
|--------|-------|
| 400 | Invalid ID format, ID mismatch, invalid material type, validation failure |
| 404 | Material not found |
| 500 | Server error |

### Validation Errors (Quiz)

- Boolean question without exactly 2 answers
- Scale question without scaleConfig
- Choice/checkboxes question with fewer than 2 answers
