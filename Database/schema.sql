CREATE TABLE IF NOT EXISTS records (
  id INT PRIMARY KEY,
  rec_date DATE,
  file_num INT,
  machine_num INT,
  Apllicant VARCHAR(200),
  Defendent VARCHAR(200),
  witness_type VARCHAR(100),
  witnesses VARCHAR(5000),
  trial VARCHAR(100),
  judge VARCHAR(100),
  audio VARCHAR(200),
  audio_status VARCHAR(50),
  remark VARCHAR(1000),
  appointed_on DATE,
  inserted_on DATE,
  recorder VARCHAR(100),
  transcriber VARCHAR(100) DEFAULT 'NOT ASSIGNED',
  status VARCHAR(500),
  distrubuted_on DATE,
  finished_date DATE,
  doc VARCHAR(200)
);

CREATE TABLE IF NOT EXISTS audio_progress (
  id INT AUTO_INCREMENT PRIMARY KEY,
  machine_num VARCHAR(500) NOT NULL,
  last_position VARCHAR(500) NOT NULL
);

CREATE TABLE IF NOT EXISTS req_acc (
  id INT PRIMARY KEY,
  username VARCHAR(100),
  full_name VARCHAR(200),
  gender VARCHAR(20),
  role VARCHAR(100),
  prof_pic VARCHAR(500),
  id_card VARCHAR(500),
  password VARCHAR(200),
  req_date DATETIME,
  status VARCHAR(50)
);

CREATE TABLE IF NOT EXISTS files (
  id INT PRIMARY KEY,
  username VARCHAR(200),
  file_number INT,
  machine_number INT,
  path VARCHAR(500)
);

CREATE INDEX IF NOT EXISTS idx_records_transcriber_status ON records (transcriber, status);
CREATE INDEX IF NOT EXISTS idx_audio_progress_machine ON audio_progress (machine_num);
CREATE INDEX IF NOT EXISTS idx_req_acc_username ON req_acc (username, status, role);
