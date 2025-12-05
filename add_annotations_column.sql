-- Migration script to add Annotations and startTime columns to Materials table
-- Run this script on existing tenant databases to add the missing columns

-- Add startTime column for VideoMaterial
ALTER TABLE `Materials`
ADD COLUMN IF NOT EXISTS `startTime` varchar(50) DEFAULT NULL AFTER `VideoResolution`;

-- Add Annotations column (shared by VideoMaterial and ImageMaterial)
ALTER TABLE `Materials`
ADD COLUMN IF NOT EXISTS `Annotations` json DEFAULT NULL AFTER `startTime`;

-- Verify the columns were added
SHOW COLUMNS FROM `Materials` LIKE 'startTime';
SHOW COLUMNS FROM `Materials` LIKE 'Annotations';
