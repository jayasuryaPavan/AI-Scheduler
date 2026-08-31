-- =============================================================
-- AI Manufacturing Scheduler — PostgreSQL Init Script
-- Creates schema and seeds mock data for a live assembly floor
-- =============================================================

-- ─────────────────────────────────────────────────────────────
-- TABLE: WorkCenters
-- Tracks machine/labor stations with capacity and availability
-- ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "WorkCenters" (
    "Id"                 SERIAL PRIMARY KEY,
    "Name"               VARCHAR(100) NOT NULL,
    "PlantName"          VARCHAR(100) NOT NULL DEFAULT 'Plant 1',
    "DailyCapacityHours" DECIMAL(5,2) NOT NULL,
    "RequiredAssociatesPerShift" INTEGER NOT NULL DEFAULT 1,
    "Status"             VARCHAR(20)  NOT NULL DEFAULT 'Active'
                             CHECK ("Status" IN ('Active', 'Down'))
);

-- ─────────────────────────────────────────────────────────────
-- TABLE: Shifts
-- Time blocks of capacity for Work Centers
-- ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "Shifts" (
    "Id"              SERIAL PRIMARY KEY,
    "WorkCenterId"    INTEGER      NOT NULL REFERENCES "WorkCenters"("Id") ON DELETE CASCADE,
    "ShiftName"       VARCHAR(50)  NOT NULL,
    "StartTime"       TIME         NOT NULL,
    "EndTime"         TIME         NOT NULL,
    "CapacityHours"   DECIMAL(5,2) NOT NULL CHECK ("CapacityHours" > 0)
);


-- ─────────────────────────────────────────────────────────────
-- TABLE: PurchaseOrders
-- Tracks inbound raw material shipments from EXTERNAL suppliers
-- ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "PurchaseOrders" (
    "Id"                   SERIAL PRIMARY KEY,
    "PartNumber"           VARCHAR(50)  NOT NULL,
    "PartDescription"      VARCHAR(200),
    "Quantity"             INTEGER      NOT NULL CHECK ("Quantity" > 0),
    "ExpectedDeliveryDate" DATE         NOT NULL,
    "SupplierLeadTimeDays" INTEGER      NOT NULL DEFAULT 0,
    "Status"               VARCHAR(20)  NOT NULL DEFAULT 'Pending'
                               CHECK ("Status" IN ('Pending', 'Received', 'Delayed', 'In-Transit', 'Out of Stock')),
    "CreatedAt"            TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

-- ─────────────────────────────────────────────────────────────
-- TABLE: TransferOrders
-- Tracks INTERNAL material movement between plants
-- ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "TransferOrders" (
    "Id"                   SERIAL PRIMARY KEY,
    "PartNumber"           VARCHAR(50)  NOT NULL,
    "PartDescription"      VARCHAR(200),
    "Quantity"             INTEGER      NOT NULL CHECK ("Quantity" > 0),
    "SourcePlant"          VARCHAR(100) NOT NULL,
    "DestinationPlant"     VARCHAR(100) NOT NULL,
    "ExpectedDeliveryDate" DATE         NOT NULL,
    "Status"               VARCHAR(20)  NOT NULL DEFAULT 'Pending'
                               CHECK ("Status" IN ('Pending', 'In-Transit', 'Received')),
    "CreatedAt"            TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

-- ─────────────────────────────────────────────────────────────
-- TABLE: Inventory
-- Tracks current on-hand raw materials per plant
-- ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "Inventory" (
    "Id"              SERIAL PRIMARY KEY,
    "PartNumber"      VARCHAR(50)  NOT NULL,
    "PartDescription" VARCHAR(200),
    "PlantName"       VARCHAR(100) NOT NULL DEFAULT 'Plant 1',
    "QuantityOnHand"  INTEGER      NOT NULL DEFAULT 0 CHECK ("QuantityOnHand" >= 0),
    "MinStockLevel"   INTEGER      NOT NULL DEFAULT 0,
    "MaxStockLevel"   INTEGER      NOT NULL DEFAULT 0,
    "SupplierName"    VARCHAR(200),
    "LeadTimeDays"    INTEGER      NOT NULL DEFAULT 7,
    "UpdatedAt"       TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_inv_part_plant ON "Inventory" ("PartNumber", "PlantName");

-- ─────────────────────────────────────────────────────────────
-- TABLE: WorkOrders
-- The parent production ticket (Job) for a finished good
-- RequiredMaterials: JSONB array of {partNumber, quantity} BOM entries
-- ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "WorkOrders" (
    "Id"                SERIAL PRIMARY KEY,
    "WorkOrderNumber"   VARCHAR(20)  NOT NULL UNIQUE,
    "FinishedGoodSku"   VARCHAR(100) NOT NULL,
    "Quantity"          INTEGER      NOT NULL CHECK ("Quantity" > 0),
    "DueDate"           DATE         NOT NULL,
    "Priority"          INTEGER      NOT NULL DEFAULT 99,
    "Status"            VARCHAR(20)  NOT NULL DEFAULT 'Scheduled'
                            CHECK ("Status" IN ('Scheduled', 'Blocked', 'In-Progress', 'Completed')),
    "RequiredMaterials" JSONB        NOT NULL DEFAULT '[]',
    "AgentNotes"        TEXT,
    "CreatedAt"         TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    "UpdatedAt"         TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_wo_status   ON "WorkOrders" ("Status");
CREATE INDEX IF NOT EXISTS idx_wo_priority ON "WorkOrders" ("Priority");

-- ─────────────────────────────────────────────────────────────
-- TABLE: WorkOrderOperations
-- The specific routing steps to complete a Work Order
-- OperationSequence: 10, 20, 30 — must execute in order
-- ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "WorkOrderOperations" (
    "Id"                   SERIAL PRIMARY KEY,
    "WorkOrderId"          INTEGER      NOT NULL REFERENCES "WorkOrders"("Id") ON DELETE CASCADE,
    "WorkCenterId"         INTEGER      NOT NULL REFERENCES "WorkCenters"("Id"),
    "OperationSequence"    INTEGER      NOT NULL,
    "OperationDescription" VARCHAR(200),
    "SetupTimeHours"       DECIMAL(5,2) NOT NULL DEFAULT 0,
    "CycleTimePerUnitHours" DECIMAL(5,2) NOT NULL DEFAULT 0,
    "Status"               VARCHAR(20)  NOT NULL DEFAULT 'Scheduled'
                               CHECK ("Status" IN ('Scheduled', 'In-Progress', 'Completed', 'Blocked'))
);

CREATE INDEX IF NOT EXISTS idx_woo_workorder  ON "WorkOrderOperations" ("WorkOrderId");
CREATE INDEX IF NOT EXISTS idx_woo_workcenter ON "WorkOrderOperations" ("WorkCenterId");

-- ─────────────────────────────────────────────────────────────
-- TABLE: ShiftActuals
-- Tracks recorded production and associates by shift supervisors
-- ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "ShiftActuals" (
    "Id"                   SERIAL PRIMARY KEY,
    "WorkOrderOperationId" INTEGER      NOT NULL REFERENCES "WorkOrderOperations"("Id") ON DELETE CASCADE,
    "ShiftName"            VARCHAR(50)  NOT NULL,
    "TimeFinished"         VARCHAR(10),
    "AssociatesWorked"     INTEGER,
    "RecordedAt"           TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_shiftactuals_op_shift ON "ShiftActuals" ("WorkOrderOperationId", "ShiftName");


-- =============================================================
-- SEED DATA — Assembly plant with multi-plant operations
-- =============================================================

-- ── Work Centers ─────────────────────────────────────────────
INSERT INTO "WorkCenters" ("Name", "PlantName", "DailyCapacityHours", "RequiredAssociatesPerShift", "Status") VALUES
('Ink Filling Station',      'Plant 1', 16.00, 1, 'Active'),
('Casing Assembly Station',  'Plant 1', 16.00, 2, 'Active'),
('Packaging Station',        'Plant 1', 16.00, 1, 'Active'),
('Brand Stamping Station',   'Plant 2', 16.00, 1, 'Active');

-- ── Shifts ───────────────────────────────────────────────────
INSERT INTO "Shifts" ("WorkCenterId", "ShiftName", "StartTime", "EndTime", "CapacityHours") VALUES
(1, 'Shift A', '06:00', '14:00', 8.00),
(1, 'Shift B', '14:00', '22:00', 8.00),
(2, 'Shift A', '06:00', '14:00', 8.00),
(2, 'Shift B', '14:00', '22:00', 8.00),
(3, 'Shift A', '06:00', '14:00', 8.00),
(3, 'Shift B', '14:00', '22:00', 8.00),
(4, 'Shift A', '06:00', '14:00', 8.00),
(4, 'Shift B', '14:00', '22:00', 8.00);

-- ── Purchase Orders (EXTERNAL suppliers) ─────────────────────
INSERT INTO "PurchaseOrders" ("PartNumber", "PartDescription", "Quantity", "ExpectedDeliveryDate", "SupplierLeadTimeDays", "Status") VALUES
('INK-BLU', 'Standard Blue Ink Barrels',        20000, '2026-08-12', 3, 'Received'),
('INK-BLK', 'Standard Black Ink Barrels',       15000, '2026-08-14', 3, 'Received'),
('INK-RED', 'Standard Red Ink Barrels',          10000, '2026-08-18', 5, 'Pending'),
('PLAST-CSG', 'Molded Plastic Casings (Clear)',  80000, '2026-08-15', 5, 'Pending'),
('PACK-BOX', 'Cardboard Packaging Boxes',        30000, '2026-08-10', 4, 'Received');

-- ── Transfer Orders (INTERNAL between plants) ────────────────
INSERT INTO "TransferOrders" ("PartNumber", "PartDescription", "Quantity", "SourcePlant", "DestinationPlant", "ExpectedDeliveryDate", "Status") VALUES
('STMP-ALPHA',  'Stamped Casings - Alpha',       2000, 'Plant 2', 'Plant 1', '2026-08-16', 'In-Transit'),
('STMP-BETA','Stamped Casings - Beta',   1500, 'Plant 2', 'Plant 1', '2026-08-18', 'Pending'),
('STMP-GAMMA',     'Stamped Casings - Gamma',           3000, 'Plant 2', 'Plant 1', '2026-08-14', 'Received'),
('STMP-DELTA',   'Stamped Casings - Delta',         1200, 'Plant 2', 'Plant 1', '2026-08-20', 'Pending');

-- ── Inventory (on-hand stock per plant) ──────────────────────
INSERT INTO "Inventory" ("PartNumber", "PartDescription", "PlantName", "QuantityOnHand", "MinStockLevel", "MaxStockLevel", "SupplierName", "LeadTimeDays") VALUES
('INK-BLU',    'Standard Blue Ink Barrels',       'Plant 1', 8000,  3000, 25000, 'InkCo Supplies',    3),
('INK-BLK',    'Standard Black Ink Barrels',      'Plant 1', 6000,  2000, 20000, 'InkCo Supplies',    3),
('INK-RED',    'Standard Red Ink Barrels',         'Plant 1',  500,  1000, 15000, 'InkCo Supplies',    5),
('PLAST-CSG',  'Molded Plastic Casings (Clear)',   'Plant 1', 20000, 10000, 80000, 'PlasticsGrp',      5),
('PACK-BOX',   'Cardboard Packaging Boxes',        'Plant 1', 5000,  2000, 20000, 'PaperMills',        4),
('STMP-BLANK', 'Blank Casings for Stamping',       'Plant 2', 15000, 5000, 30000, 'PlasticsGrp',       5);

-- ── Work Orders (10 brands × 5 models = 50) ─────────────────
INSERT INTO "WorkOrders" ("WorkOrderNumber", "FinishedGoodSku", "Quantity", "DueDate", "Priority", "Status", "RequiredMaterials") VALUES
('WO-2026-001', 'ALPHA-BASIC',      500, '2026-09-12', 1,  'In-Progress', '[{"partNumber":"INK-BLU","quantity":500},{"partNumber":"PLAST-CSG","quantity":500},{"partNumber":"STMP-ALPHA","quantity":500},{"partNumber":"PACK-BOX","quantity":500}]'),
('WO-2026-002', 'ALPHA-PRO',        300, '2026-09-12', 2,  'Scheduled',   '[{"partNumber":"INK-BLK","quantity":300},{"partNumber":"PLAST-CSG","quantity":300},{"partNumber":"STMP-ALPHA","quantity":300},{"partNumber":"PACK-BOX","quantity":300}]'),
('WO-2026-003', 'ALPHA-EXECUTIVE',  200, '2026-09-12', 3,  'Scheduled',   '[{"partNumber":"INK-BLK","quantity":200},{"partNumber":"PLAST-CSG","quantity":200},{"partNumber":"STMP-ALPHA","quantity":200},{"partNumber":"PACK-BOX","quantity":200}]'),
('WO-2026-004', 'ALPHA-SPORT',      700, '2026-09-12', 4,  'Scheduled',   '[{"partNumber":"INK-BLU","quantity":700},{"partNumber":"PLAST-CSG","quantity":700},{"partNumber":"STMP-ALPHA","quantity":700},{"partNumber":"PACK-BOX","quantity":700}]'),
('WO-2026-005', 'ALPHA-MINI',       400, '2026-09-12', 5,  'Scheduled',   '[{"partNumber":"INK-RED","quantity":400},{"partNumber":"PLAST-CSG","quantity":400},{"partNumber":"STMP-ALPHA","quantity":400},{"partNumber":"PACK-BOX","quantity":400}]'),
('WO-2026-006', 'BETA-BASIC',   300, '2026-09-12', 6,  'Scheduled',   '[{"partNumber":"INK-BLK","quantity":300},{"partNumber":"PLAST-CSG","quantity":300},{"partNumber":"STMP-BETA","quantity":300},{"partNumber":"PACK-BOX","quantity":300}]'),
('WO-2026-007', 'BETA-PRO',     400, '2026-09-12', 7,  'Scheduled',   '[{"partNumber":"INK-BLU","quantity":400},{"partNumber":"PLAST-CSG","quantity":400},{"partNumber":"STMP-BETA","quantity":400},{"partNumber":"PACK-BOX","quantity":400}]'),
('WO-2026-008', 'BETA-EXECUTIVE',200,'2026-09-12', 8,  'Scheduled',   '[{"partNumber":"INK-BLK","quantity":200},{"partNumber":"PLAST-CSG","quantity":200},{"partNumber":"STMP-BETA","quantity":200},{"partNumber":"PACK-BOX","quantity":200}]'),
('WO-2026-009', 'BETA-SPORT',   300, '2026-09-12', 9,  'Scheduled',   '[{"partNumber":"INK-BLU","quantity":300},{"partNumber":"PLAST-CSG","quantity":300},{"partNumber":"STMP-BETA","quantity":300},{"partNumber":"PACK-BOX","quantity":300}]'),
('WO-2026-010', 'BETA-MINI',    100, '2026-09-12', 10, 'Scheduled',   '[{"partNumber":"INK-RED","quantity":100},{"partNumber":"PLAST-CSG","quantity":100},{"partNumber":"STMP-BETA","quantity":100},{"partNumber":"PACK-BOX","quantity":100}]'),
('WO-2026-011', 'GAMMA-BASIC',         900, '2026-09-12', 11, 'Scheduled',   '[{"partNumber":"INK-BLU","quantity":900},{"partNumber":"PLAST-CSG","quantity":900},{"partNumber":"STMP-GAMMA","quantity":900},{"partNumber":"PACK-BOX","quantity":900}]'),
('WO-2026-012', 'GAMMA-PRO',           600, '2026-09-12', 12, 'Scheduled',   '[{"partNumber":"INK-BLK","quantity":600},{"partNumber":"PLAST-CSG","quantity":600},{"partNumber":"STMP-GAMMA","quantity":600},{"partNumber":"PACK-BOX","quantity":600}]'),
('WO-2026-013', 'GAMMA-EXECUTIVE',     200, '2026-09-12', 13, 'Scheduled',   '[{"partNumber":"INK-BLK","quantity":200},{"partNumber":"PLAST-CSG","quantity":200},{"partNumber":"STMP-GAMMA","quantity":200},{"partNumber":"PACK-BOX","quantity":200}]'),
('WO-2026-014', 'GAMMA-SPORT',         400, '2026-09-12', 14, 'Scheduled',   '[{"partNumber":"INK-BLU","quantity":400},{"partNumber":"PLAST-CSG","quantity":400},{"partNumber":"STMP-GAMMA","quantity":400},{"partNumber":"PACK-BOX","quantity":400}]'),
('WO-2026-015', 'GAMMA-MINI',          300, '2026-09-12', 15, 'Scheduled',   '[{"partNumber":"INK-RED","quantity":300},{"partNumber":"PLAST-CSG","quantity":300},{"partNumber":"STMP-GAMMA","quantity":300},{"partNumber":"PACK-BOX","quantity":300}]'),
('WO-2026-016', 'DELTA-BASIC',       500, '2026-09-12', 16, 'Scheduled',   '[{"partNumber":"INK-BLU","quantity":500},{"partNumber":"PLAST-CSG","quantity":500},{"partNumber":"STMP-DELTA","quantity":500},{"partNumber":"PACK-BOX","quantity":500}]'),
('WO-2026-017', 'DELTA-PRO',         300, '2026-09-12', 17, 'Scheduled',   '[{"partNumber":"INK-BLK","quantity":300},{"partNumber":"PLAST-CSG","quantity":300},{"partNumber":"STMP-DELTA","quantity":300},{"partNumber":"PACK-BOX","quantity":300}]'),
('WO-2026-018', 'DELTA-EXECUTIVE',   200, '2026-09-12', 18, 'Scheduled',   '[{"partNumber":"INK-BLK","quantity":200},{"partNumber":"PLAST-CSG","quantity":200},{"partNumber":"STMP-DELTA","quantity":200},{"partNumber":"PACK-BOX","quantity":200}]'),
('WO-2026-019', 'DELTA-SPORT',       400, '2026-09-12', 19, 'Scheduled',   '[{"partNumber":"INK-BLU","quantity":400},{"partNumber":"PLAST-CSG","quantity":400},{"partNumber":"STMP-DELTA","quantity":400},{"partNumber":"PACK-BOX","quantity":400}]'),
('WO-2026-020', 'DELTA-MINI',        100, '2026-09-12', 20, 'Scheduled',   '[{"partNumber":"INK-RED","quantity":100},{"partNumber":"PLAST-CSG","quantity":100},{"partNumber":"STMP-DELTA","quantity":100},{"partNumber":"PACK-BOX","quantity":100}]'),
('WO-2026-021', 'EPSILON-BASIC',       400, '2026-09-12', 21, 'Scheduled',   '[{"partNumber":"INK-BLU","quantity":400},{"partNumber":"PLAST-CSG","quantity":400},{"partNumber":"STMP-EPSILON","quantity":400},{"partNumber":"PACK-BOX","quantity":400}]'),
('WO-2026-022', 'EPSILON-PRO',         300, '2026-09-12', 22, 'Scheduled',   '[{"partNumber":"INK-BLK","quantity":300},{"partNumber":"PLAST-CSG","quantity":300},{"partNumber":"STMP-EPSILON","quantity":300},{"partNumber":"PACK-BOX","quantity":300}]'),
('WO-2026-023', 'EPSILON-EXECUTIVE',   200, '2026-09-12', 23, 'Scheduled',   '[{"partNumber":"INK-BLK","quantity":200},{"partNumber":"PLAST-CSG","quantity":200},{"partNumber":"STMP-EPSILON","quantity":200},{"partNumber":"PACK-BOX","quantity":200}]'),
('WO-2026-024', 'EPSILON-SPORT',       500, '2026-09-12', 24, 'Scheduled',   '[{"partNumber":"INK-BLU","quantity":500},{"partNumber":"PLAST-CSG","quantity":500},{"partNumber":"STMP-EPSILON","quantity":500},{"partNumber":"PACK-BOX","quantity":500}]'),
('WO-2026-025', 'EPSILON-MINI',        100, '2026-09-12', 25, 'Scheduled',   '[{"partNumber":"INK-RED","quantity":100},{"partNumber":"PLAST-CSG","quantity":100},{"partNumber":"STMP-EPSILON","quantity":100},{"partNumber":"PACK-BOX","quantity":100}]'),
('WO-2026-026', 'ZETA-BASIC',        600, '2026-09-12', 26, 'Scheduled',   '[{"partNumber":"INK-BLU","quantity":600},{"partNumber":"PLAST-CSG","quantity":600},{"partNumber":"STMP-ZETA","quantity":600},{"partNumber":"PACK-BOX","quantity":600}]'),
('WO-2026-027', 'ZETA-PRO',          400, '2026-09-12', 27, 'Scheduled',   '[{"partNumber":"INK-BLK","quantity":400},{"partNumber":"PLAST-CSG","quantity":400},{"partNumber":"STMP-ZETA","quantity":400},{"partNumber":"PACK-BOX","quantity":400}]'),
('WO-2026-028', 'ZETA-EXECUTIVE',    200, '2026-09-12', 28, 'Scheduled',   '[{"partNumber":"INK-BLK","quantity":200},{"partNumber":"PLAST-CSG","quantity":200},{"partNumber":"STMP-ZETA","quantity":200},{"partNumber":"PACK-BOX","quantity":200}]'),
('WO-2026-029', 'ZETA-SPORT',        300, '2026-09-12', 29, 'Scheduled',   '[{"partNumber":"INK-BLU","quantity":300},{"partNumber":"PLAST-CSG","quantity":300},{"partNumber":"STMP-ZETA","quantity":300},{"partNumber":"PACK-BOX","quantity":300}]'),
('WO-2026-030', 'ZETA-MINI',         100, '2026-09-12', 30, 'Scheduled',   '[{"partNumber":"INK-RED","quantity":100},{"partNumber":"PLAST-CSG","quantity":100},{"partNumber":"STMP-ZETA","quantity":100},{"partNumber":"PACK-BOX","quantity":100}]'),
('WO-2026-031', 'ETA-BASIC',    500, '2026-09-12', 31, 'Scheduled',   '[{"partNumber":"INK-BLU","quantity":500},{"partNumber":"PLAST-CSG","quantity":500},{"partNumber":"STMP-ETA","quantity":500},{"partNumber":"PACK-BOX","quantity":500}]'),
('WO-2026-032', 'ETA-PRO',      300, '2026-09-12', 32, 'Scheduled',   '[{"partNumber":"INK-BLK","quantity":300},{"partNumber":"PLAST-CSG","quantity":300},{"partNumber":"STMP-ETA","quantity":300},{"partNumber":"PACK-BOX","quantity":300}]'),
('WO-2026-033', 'ETA-EXECUTIVE',200, '2026-09-12', 33, 'Scheduled',   '[{"partNumber":"INK-BLK","quantity":200},{"partNumber":"PLAST-CSG","quantity":200},{"partNumber":"STMP-ETA","quantity":200},{"partNumber":"PACK-BOX","quantity":200}]'),
('WO-2026-034', 'ETA-SPORT',    400, '2026-09-12', 34, 'Scheduled',   '[{"partNumber":"INK-BLU","quantity":400},{"partNumber":"PLAST-CSG","quantity":400},{"partNumber":"STMP-ETA","quantity":400},{"partNumber":"PACK-BOX","quantity":400}]'),
('WO-2026-035', 'ETA-MINI',     100, '2026-09-12', 35, 'Scheduled',   '[{"partNumber":"INK-RED","quantity":100},{"partNumber":"PLAST-CSG","quantity":100},{"partNumber":"STMP-ETA","quantity":100},{"partNumber":"PACK-BOX","quantity":100}]'),
('WO-2026-036', 'THETA-BASIC',     400, '2026-09-12', 36, 'Scheduled',   '[{"partNumber":"INK-BLU","quantity":400},{"partNumber":"PLAST-CSG","quantity":400},{"partNumber":"STMP-THETA","quantity":400},{"partNumber":"PACK-BOX","quantity":400}]'),
('WO-2026-037', 'THETA-PRO',       300, '2026-09-12', 37, 'Scheduled',   '[{"partNumber":"INK-BLK","quantity":300},{"partNumber":"PLAST-CSG","quantity":300},{"partNumber":"STMP-THETA","quantity":300},{"partNumber":"PACK-BOX","quantity":300}]'),
('WO-2026-038', 'THETA-EXECUTIVE', 200, '2026-09-12', 38, 'Scheduled',   '[{"partNumber":"INK-BLK","quantity":200},{"partNumber":"PLAST-CSG","quantity":200},{"partNumber":"STMP-THETA","quantity":200},{"partNumber":"PACK-BOX","quantity":200}]'),
('WO-2026-039', 'THETA-SPORT',     500, '2026-09-12', 39, 'Scheduled',   '[{"partNumber":"INK-BLU","quantity":500},{"partNumber":"PLAST-CSG","quantity":500},{"partNumber":"STMP-THETA","quantity":500},{"partNumber":"PACK-BOX","quantity":500}]'),
('WO-2026-040', 'THETA-MINI',      100, '2026-09-12', 40, 'Scheduled',   '[{"partNumber":"INK-RED","quantity":100},{"partNumber":"PLAST-CSG","quantity":100},{"partNumber":"STMP-THETA","quantity":100},{"partNumber":"PACK-BOX","quantity":100}]'),
('WO-2026-041', 'IOTA-BASIC',    300, '2026-09-12', 41, 'Scheduled',   '[{"partNumber":"INK-BLU","quantity":300},{"partNumber":"PLAST-CSG","quantity":300},{"partNumber":"STMP-IOTA","quantity":300},{"partNumber":"PACK-BOX","quantity":300}]'),
('WO-2026-042', 'IOTA-PRO',      200, '2026-09-12', 42, 'Scheduled',   '[{"partNumber":"INK-BLK","quantity":200},{"partNumber":"PLAST-CSG","quantity":200},{"partNumber":"STMP-IOTA","quantity":200},{"partNumber":"PACK-BOX","quantity":200}]'),
('WO-2026-043', 'IOTA-EXECUTIVE',100, '2026-09-12', 43, 'Scheduled',   '[{"partNumber":"INK-BLK","quantity":100},{"partNumber":"PLAST-CSG","quantity":100},{"partNumber":"STMP-IOTA","quantity":100},{"partNumber":"PACK-BOX","quantity":100}]'),
('WO-2026-044', 'IOTA-SPORT',    400, '2026-09-12', 44, 'Scheduled',   '[{"partNumber":"INK-BLU","quantity":400},{"partNumber":"PLAST-CSG","quantity":400},{"partNumber":"STMP-IOTA","quantity":400},{"partNumber":"PACK-BOX","quantity":400}]'),
('WO-2026-045', 'IOTA-MINI',     100, '2026-09-12', 45, 'Scheduled',   '[{"partNumber":"INK-RED","quantity":100},{"partNumber":"PLAST-CSG","quantity":100},{"partNumber":"STMP-IOTA","quantity":100},{"partNumber":"PACK-BOX","quantity":100}]'),
('WO-2026-046', 'KAPPA-BASIC',500,'2026-09-12', 46, 'Scheduled',   '[{"partNumber":"INK-BLU","quantity":500},{"partNumber":"PLAST-CSG","quantity":500},{"partNumber":"STMP-KAPPA","quantity":500},{"partNumber":"PACK-BOX","quantity":500}]'),
('WO-2026-047', 'KAPPA-PRO',  300, '2026-09-12', 47, 'Scheduled',   '[{"partNumber":"INK-BLK","quantity":300},{"partNumber":"PLAST-CSG","quantity":300},{"partNumber":"STMP-KAPPA","quantity":300},{"partNumber":"PACK-BOX","quantity":300}]'),
('WO-2026-048', 'KAPPA-EXECUTIVE',200,'2026-09-12',48,'Scheduled',  '[{"partNumber":"INK-BLK","quantity":200},{"partNumber":"PLAST-CSG","quantity":200},{"partNumber":"STMP-KAPPA","quantity":200},{"partNumber":"PACK-BOX","quantity":200}]'),
('WO-2026-049', 'KAPPA-SPORT',400, '2026-09-12', 49, 'Scheduled',   '[{"partNumber":"INK-BLU","quantity":400},{"partNumber":"PLAST-CSG","quantity":400},{"partNumber":"STMP-KAPPA","quantity":400},{"partNumber":"PACK-BOX","quantity":400}]'),
('WO-2026-050', 'KAPPA-MINI', 100, '2026-09-12', 50, 'Scheduled',   '[{"partNumber":"INK-RED","quantity":100},{"partNumber":"PLAST-CSG","quantity":100},{"partNumber":"STMP-KAPPA","quantity":100},{"partNumber":"PACK-BOX","quantity":100}]');

-- ── Work Order Operations (Routing) ──────────────────────────
-- Op 10: Fill ink (Plant 1, WC 1)
-- Op 20: Assemble casing (Plant 1, WC 2)
-- Op 30: Stamp brand (Plant 2, WC 4) — requires Transfer Order
-- Op 40: Package (Plant 1, WC 3)
-- For In-Progress WOs: Op 10 is In-Progress, rest Scheduled
-- For Scheduled WOs: All ops Scheduled
INSERT INTO "WorkOrderOperations" ("WorkOrderId", "WorkCenterId", "OperationSequence", "OperationDescription", "SetupTimeHours", "CycleTimePerUnitHours", "Status") VALUES
(1, 1, 10, 'Fill blue ink barrels',       1.0, 0.01, 'In-Progress'),
(1, 2, 20, 'Assemble plastic casing',     1.5, 0.02, 'Scheduled'),
(1, 4, 30, 'Stamp Alpha brand name',     0.5, 0.01, 'Scheduled'),
(1, 3, 40, 'Package into retail boxes',   0.5, 0.02, 'Scheduled'),
(2, 1, 10, 'Fill black ink barrels',      1.0, 0.01, 'Scheduled'),
(2, 2, 20, 'Assemble plastic casing',     1.5, 0.02, 'Scheduled'),
(2, 4, 30, 'Stamp Alpha brand name',     0.5, 0.01, 'Scheduled'),
(2, 3, 40, 'Package into retail boxes',   0.5, 0.02, 'Scheduled'),
(3, 1, 10, 'Fill black ink barrels',      1.0, 0.01, 'Scheduled'),
(3, 2, 20, 'Assemble plastic casing',     1.5, 0.02, 'Scheduled'),
(3, 4, 30, 'Stamp Alpha brand name',     0.5, 0.01, 'Scheduled'),
(3, 3, 40, 'Package into retail boxes',   0.5, 0.02, 'Scheduled'),
(4, 1, 10, 'Fill blue ink barrels',       1.0, 0.01, 'Scheduled'),
(4, 2, 20, 'Assemble plastic casing',     1.5, 0.02, 'Scheduled'),
(4, 4, 30, 'Stamp Alpha brand name',     0.5, 0.01, 'Scheduled'),
(4, 3, 40, 'Package into retail boxes',   0.5, 0.02, 'Scheduled'),
(5, 1, 10, 'Fill red ink barrels',        1.0, 0.01, 'Scheduled'),
(5, 2, 20, 'Assemble plastic casing',     1.5, 0.02, 'Scheduled'),
(5, 4, 30, 'Stamp Alpha brand name',     0.5, 0.01, 'Scheduled'),
(5, 3, 40, 'Package into retail boxes',   0.5, 0.02, 'Scheduled'),
(6, 1, 10, 'Fill black ink barrels',      1.0, 0.01, 'Scheduled'),
(6, 2, 20, 'Assemble plastic casing',     1.5, 0.02, 'Scheduled'),
(6, 4, 30, 'Stamp Beta brand name',  0.5, 0.01, 'Scheduled'),
(6, 3, 40, 'Package into retail boxes',   0.5, 0.02, 'Scheduled'),
(7, 1, 10, 'Fill blue ink barrels',       1.0, 0.01, 'Scheduled'),
(7, 2, 20, 'Assemble plastic casing',     1.5, 0.02, 'Scheduled'),
(7, 4, 30, 'Stamp Beta brand name',  0.5, 0.01, 'Scheduled'),
(7, 3, 40, 'Package into retail boxes',   0.5, 0.02, 'Scheduled'),
(8, 1, 10, 'Fill black ink barrels',      1.0, 0.01, 'Scheduled'),
(8, 2, 20, 'Assemble plastic casing',     1.5, 0.02, 'Scheduled'),
(8, 4, 30, 'Stamp Beta brand name',  0.5, 0.01, 'Scheduled'),
(8, 3, 40, 'Package into retail boxes',   0.5, 0.02, 'Scheduled'),
(9, 1, 10, 'Fill blue ink barrels',       1.0, 0.01, 'Scheduled'),
(9, 2, 20, 'Assemble plastic casing',     1.5, 0.02, 'Scheduled'),
(9, 4, 30, 'Stamp Beta brand name',  0.5, 0.01, 'Scheduled'),
(9, 3, 40, 'Package into retail boxes',   0.5, 0.02, 'Scheduled'),
(10, 1, 10, 'Fill red ink barrels',       1.0, 0.01, 'Scheduled'),
(10, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(10, 4, 30, 'Stamp Beta brand name', 0.5, 0.01, 'Scheduled'),
(10, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(11, 1, 10, 'Fill blue ink barrels',      1.0, 0.01, 'Scheduled'),
(11, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(11, 4, 30, 'Stamp Gamma brand name',       0.5, 0.01, 'Scheduled'),
(11, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(12, 1, 10, 'Fill black ink barrels',     1.0, 0.01, 'Scheduled'),
(12, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(12, 4, 30, 'Stamp Gamma brand name',       0.5, 0.01, 'Scheduled'),
(12, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(13, 1, 10, 'Fill black ink barrels',     1.0, 0.01, 'Scheduled'),
(13, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(13, 4, 30, 'Stamp Gamma brand name',       0.5, 0.01, 'Scheduled'),
(13, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(14, 1, 10, 'Fill blue ink barrels',      1.0, 0.01, 'Scheduled'),
(14, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(14, 4, 30, 'Stamp Gamma brand name',       0.5, 0.01, 'Scheduled'),
(14, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(15, 1, 10, 'Fill red ink barrels',       1.0, 0.01, 'Scheduled'),
(15, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(15, 4, 30, 'Stamp Gamma brand name',       0.5, 0.01, 'Scheduled'),
(15, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(16, 1, 10, 'Fill blue ink barrels',      1.0, 0.01, 'Scheduled'),
(16, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(16, 4, 30, 'Stamp Delta brand name',     0.5, 0.01, 'Scheduled'),
(16, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(17, 1, 10, 'Fill black ink barrels',     1.0, 0.01, 'Scheduled'),
(17, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(17, 4, 30, 'Stamp Delta brand name',     0.5, 0.01, 'Scheduled'),
(17, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(18, 1, 10, 'Fill black ink barrels',     1.0, 0.01, 'Scheduled'),
(18, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(18, 4, 30, 'Stamp Delta brand name',     0.5, 0.01, 'Scheduled'),
(18, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(19, 1, 10, 'Fill blue ink barrels',      1.0, 0.01, 'Scheduled'),
(19, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(19, 4, 30, 'Stamp Delta brand name',     0.5, 0.01, 'Scheduled'),
(19, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(20, 1, 10, 'Fill red ink barrels',       1.0, 0.01, 'Scheduled'),
(20, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(20, 4, 30, 'Stamp Delta brand name',     0.5, 0.01, 'Scheduled'),
(20, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
-- Epsilon (WO 21-25)
(21, 1, 10, 'Fill blue ink barrels',      1.0, 0.01, 'Scheduled'),
(21, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(21, 4, 30, 'Stamp Epsilon brand name',     0.5, 0.01, 'Scheduled'),
(21, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(22, 1, 10, 'Fill black ink barrels',     1.0, 0.01, 'Scheduled'),
(22, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(22, 4, 30, 'Stamp Epsilon brand name',     0.5, 0.01, 'Scheduled'),
(22, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(23, 1, 10, 'Fill black ink barrels',     1.0, 0.01, 'Scheduled'),
(23, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(23, 4, 30, 'Stamp Epsilon brand name',     0.5, 0.01, 'Scheduled'),
(23, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(24, 1, 10, 'Fill blue ink barrels',      1.0, 0.01, 'Scheduled'),
(24, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(24, 4, 30, 'Stamp Epsilon brand name',     0.5, 0.01, 'Scheduled'),
(24, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(25, 1, 10, 'Fill red ink barrels',       1.0, 0.01, 'Scheduled'),
(25, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(25, 4, 30, 'Stamp Epsilon brand name',     0.5, 0.01, 'Scheduled'),
(25, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
-- Zeta (WO 26-30)
(26, 1, 10, 'Fill blue ink barrels',      1.0, 0.01, 'Scheduled'),
(26, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(26, 4, 30, 'Stamp Zeta brand name',      0.5, 0.01, 'Scheduled'),
(26, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(27, 1, 10, 'Fill black ink barrels',     1.0, 0.01, 'Scheduled'),
(27, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(27, 4, 30, 'Stamp Zeta brand name',      0.5, 0.01, 'Scheduled'),
(27, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(28, 1, 10, 'Fill black ink barrels',     1.0, 0.01, 'Scheduled'),
(28, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(28, 4, 30, 'Stamp Zeta brand name',      0.5, 0.01, 'Scheduled'),
(28, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(29, 1, 10, 'Fill blue ink barrels',      1.0, 0.01, 'Scheduled'),
(29, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(29, 4, 30, 'Stamp Zeta brand name',      0.5, 0.01, 'Scheduled'),
(29, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(30, 1, 10, 'Fill red ink barrels',       1.0, 0.01, 'Scheduled'),
(30, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(30, 4, 30, 'Stamp Zeta brand name',      0.5, 0.01, 'Scheduled'),
(30, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
-- Eta (WO 31-35)
(31, 1, 10, 'Fill blue ink barrels',      1.0, 0.01, 'Scheduled'),
(31, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(31, 4, 30, 'Stamp Eta brand name',  0.5, 0.01, 'Scheduled'),
(31, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(32, 1, 10, 'Fill black ink barrels',     1.0, 0.01, 'Scheduled'),
(32, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(32, 4, 30, 'Stamp Eta brand name',  0.5, 0.01, 'Scheduled'),
(32, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(33, 1, 10, 'Fill black ink barrels',     1.0, 0.01, 'Scheduled'),
(33, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(33, 4, 30, 'Stamp Eta brand name',  0.5, 0.01, 'Scheduled'),
(33, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(34, 1, 10, 'Fill blue ink barrels',      1.0, 0.01, 'Scheduled'),
(34, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(34, 4, 30, 'Stamp Eta brand name',  0.5, 0.01, 'Scheduled'),
(34, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(35, 1, 10, 'Fill red ink barrels',       1.0, 0.01, 'Scheduled'),
(35, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(35, 4, 30, 'Stamp Eta brand name',  0.5, 0.01, 'Scheduled'),
(35, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
-- Theta (WO 36-40)
(36, 1, 10, 'Fill blue ink barrels',      1.0, 0.01, 'Scheduled'),
(36, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(36, 4, 30, 'Stamp Theta brand name',   0.5, 0.01, 'Scheduled'),
(36, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(37, 1, 10, 'Fill black ink barrels',     1.0, 0.01, 'Scheduled'),
(37, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(37, 4, 30, 'Stamp Theta brand name',   0.5, 0.01, 'Scheduled'),
(37, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(38, 1, 10, 'Fill black ink barrels',     1.0, 0.01, 'Scheduled'),
(38, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(38, 4, 30, 'Stamp Theta brand name',   0.5, 0.01, 'Scheduled'),
(38, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(39, 1, 10, 'Fill blue ink barrels',      1.0, 0.01, 'Scheduled'),
(39, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(39, 4, 30, 'Stamp Theta brand name',   0.5, 0.01, 'Scheduled'),
(39, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(40, 1, 10, 'Fill red ink barrels',       1.0, 0.01, 'Scheduled'),
(40, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(40, 4, 30, 'Stamp Theta brand name',   0.5, 0.01, 'Scheduled'),
(40, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
-- Iota (WO 41-45)
(41, 1, 10, 'Fill blue ink barrels',      1.0, 0.01, 'Scheduled'),
(41, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(41, 4, 30, 'Stamp Iota brand name',  0.5, 0.01, 'Scheduled'),
(41, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(42, 1, 10, 'Fill black ink barrels',     1.0, 0.01, 'Scheduled'),
(42, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(42, 4, 30, 'Stamp Iota brand name',  0.5, 0.01, 'Scheduled'),
(42, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(43, 1, 10, 'Fill black ink barrels',     1.0, 0.01, 'Scheduled'),
(43, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(43, 4, 30, 'Stamp Iota brand name',  0.5, 0.01, 'Scheduled'),
(43, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(44, 1, 10, 'Fill blue ink barrels',      1.0, 0.01, 'Scheduled'),
(44, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(44, 4, 30, 'Stamp Iota brand name',  0.5, 0.01, 'Scheduled'),
(44, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(45, 1, 10, 'Fill red ink barrels',       1.0, 0.01, 'Scheduled'),
(45, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(45, 4, 30, 'Stamp Iota brand name',  0.5, 0.01, 'Scheduled'),
(45, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
-- Kappa (WO 46-50)
(46, 1, 10, 'Fill blue ink barrels',      1.0, 0.01, 'Scheduled'),
(46, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(46, 4, 30, 'Stamp Kappa brand',   0.5, 0.01, 'Scheduled'),
(46, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(47, 1, 10, 'Fill black ink barrels',     1.0, 0.01, 'Scheduled'),
(47, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(47, 4, 30, 'Stamp Kappa brand',   0.5, 0.01, 'Scheduled'),
(47, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(48, 1, 10, 'Fill black ink barrels',     1.0, 0.01, 'Scheduled'),
(48, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(48, 4, 30, 'Stamp Kappa brand',   0.5, 0.01, 'Scheduled'),
(48, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(49, 1, 10, 'Fill blue ink barrels',      1.0, 0.01, 'Scheduled'),
(49, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(49, 4, 30, 'Stamp Kappa brand',   0.5, 0.01, 'Scheduled'),
(49, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled'),
(50, 1, 10, 'Fill red ink barrels',       1.0, 0.01, 'Scheduled'),
(50, 2, 20, 'Assemble plastic casing',    1.5, 0.02, 'Scheduled'),
(50, 4, 30, 'Stamp Kappa brand',   0.5, 0.01, 'Scheduled'),
(50, 3, 40, 'Package into retail boxes',  0.5, 0.02, 'Scheduled');

-- =============================================================
-- VECTOR DATABASE — Chat Memory with pgvector
-- =============================================================

CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS "ChatConversations" (
    "Id"          SERIAL PRIMARY KEY,
    "SessionId"   VARCHAR(100) NOT NULL,
    "Role"        VARCHAR(20)  NOT NULL CHECK ("Role" IN ('user', 'assistant')),
    "Content"     TEXT         NOT NULL,
    "ToolCalls"   JSONB        NOT NULL DEFAULT '[]',
    "Embedding"   vector(768),
    "CreatedAt"   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_chat_session ON "ChatConversations" ("SessionId", "CreatedAt");
CREATE INDEX IF NOT EXISTS idx_chat_embedding ON "ChatConversations"
    USING hnsw ("Embedding" vector_cosine_ops);

 
 C R E A T E   T A B L E   " A g e n t M e m o r i e s "   ( 
         " I d "   S E R I A L   P R I M A R Y   K E Y , 
         " M e m o r y T e x t "   T E X T   N O T   N U L L , 
         " E m b e d d i n g "   v e c t o r ( 7 6 8 ) , 
         " C r e a t e d A t "   T I M E S T A M P   W I T H   T I M E   Z O N E   N O T   N U L L   D E F A U L T   C U R R E N T _ T I M E S T A M P 
 ) ; 
 
 
 

ALTER TABLE "ChatConversations" ADD COLUMN IF NOT EXISTS "RequiresApproval" BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE "ChatConversations" ADD COLUMN IF NOT EXISTS "ProposalId" VARCHAR(20);
ALTER TABLE "ChatConversations" ADD COLUMN IF NOT EXISTS "SimulatedImpact" JSONB NOT NULL DEFAULT '[]';
ALTER TABLE "ChatConversations" ADD COLUMN IF NOT EXISTS "ApprovalStatus" VARCHAR(20) NOT NULL DEFAULT '';

