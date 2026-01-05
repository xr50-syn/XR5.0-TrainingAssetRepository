

## System Goals

- Allow users to answer **materials (quizzes)**
- Materials may or may not belong to **programs**
- Calculate **score** and **progress**
- Store a detailed history of user answers

---

## Design Principles

- The frontend sends **raw answers only**
- The backend validates, evaluates, and calculates everything
- Progress and completion are **calculated**
- Score is treated as **cache**
- Detailed JSON is treated as **historical data**

> Never trust scores sent from the client  
> Never store “program completed” in the database

---

## Core Concepts

### Entities

- **User**
- **Program** (optional)
- **Material** (quiz)
- **Question**
- **Answer**

### Rules
- A material may exist **outside of a program**
- A program contains multiple materials
- A user may partially complete a program

---

## Data Model

### 1. `user_material_data`  
**Detailed data (source of truth)**

```text
user_material_data
------------------
id
user_id
program_id     (nullable)
material_id
data           JSON
created_at
updated_at

UNIQUE (user_id, material_id)
```

Stores:
- user answers
- per-question evaluation
- awarded score
- metadata (timestamps, version)

---

### 2. `user_material_scores`  
**Summarized state (hot data)**

```text
user_material_scores
--------------------
user_id
program_id     (nullable)
material_id
score
progress       INT (0–100)
updated_at

UNIQUE (user_id, material_id)
```

Stores:
- final material score
- progress in the context of a program

---

## Complete Workflow (End-to-End)

### 1️⃣ Fetch material (quiz)

The frontend fetches the material and its questions.

```http
GET /api/materials/{material_id}
```

Response (simplified example):

```json
{
  "id": 7,
  "name": "Quiz",
  "type": "quiz",
  "config": {
    "questions": [
      {
        "id": 29,
        "questionType": "boolean",
        "text": "Question 1",
        "description": "Question description",
        "helpText": null,
        "score": 5,
        "answers": [
          { 
            "id": 37, 
            "text": "True",
            "correctAnswer": true,
            "displayOrder": 1
          },
          { 
            "id": 38, 
            "text": "False",
            "correctAnswer": true,
            "displayOrder": 2
          }
        ]
      }
    ]
  }
}
```

---

### 2️⃣ Submit answers (input)

```http
POST /api/materials/{material_id}/submit
```

> `user_id` comes from the auth token  
> `program_id` is optional

#### Input JSON (raw answers)

```json
{
  "program_id": 2,
  "questions": [
    {
      "question_id": 29,
      "answer": {
        "answer_ids": [37]
      }
    },
    {
      "question_id": 28,
      "answer": {
        "value": 4
      }
    },
    {
      "question_id": 27,
      "answer": {
        "value": "Some text"
      }
    }
  ]
}
```

Supported answer types:
- `answer_ids` → choice, boolean, multiple
- `value` → scale
- `text` → free text (future)

---

### 3️⃣ Internal processing

For each question:
1. Validate existence
2. Validate type
3. Evaluate answer
4. Calculate awarded score

Pseudocode:

```text
total_score = 0

for question:
  validate
  awarded_score = evaluate()
  total_score += awarded_score
```

---

### 4️⃣ Processed JSON (stored)

Field `user_material_data.data`:

```json
{
  "version": 1,
  "submitted_at": "2025-12-29T10:30:00Z",
  "answers": [
    {
      "question_id": 29,
      "type": "boolean",
      "answer_ids": [37],
      "score_awarded": 5
    },
    {
      "question_id": 28,
      "type": "scale",
      "value": 4,
      "score_awarded": 0
    },
    {
      "question_id": 27,
      "type": "text",
      "value": "Some text",
      "score_awarded": 0
    }
  ],
  "total_score": 5
}
```

---

### 5️⃣ Progress calculation

#### Material without program
```text
progress = 100
```

#### Material within a program

```text
progress = (answered materials / total materials in program) * 100
```

Program completion is:

```text
progress == 100
```


---

### 6️⃣ Database writes

1. `user_material_data`  
   - processed JSON
2. `user_material_scores`  
   - score
   - progress

---

### 7️⃣ Endpoint response (output)

```json
{
  "success": true,
  "material_id": 7,
  "program_id": 2,
  "score": 5,
  "progress": 50
}
```

---

## Read Paths

### 🔹 1. Overall progress (overview)

```http
GET /api/users/progress
```

Response:

```json
{
  "id": "1",
  "name": "user_name_1",
  "progress": 100,
  "programs": [
    {
      "id": "1",
      "name": "Test",
      "materials": [
        {
          "id": "1",
          "name": "Questionnaire 1",
          "type": "quiz",
          "score": 80
        },
        {
          "id": "2",
          "name": "Questionnaire 2",
          "type": "quiz",
          "score": 40
        }
      ]
    }
  ]
}
```

📌 Uses only `user_material_scores`

---

### 🔹 2. Detailed data for a material

```http
GET /api/users/{user_id}/materials/{material_id}
```

```json
{
  "user_id": 1,
  "program_id": 2,
  "material_id": 7,
  "score": 5,
  "data": { ...processed json... }
}
```

---

### 🔹 3. Detailed data for a program

```http
GET /api/users/{user_id}/programs/{program_id}/materials
```

Returns all materials in the program with detailed `data`.

---