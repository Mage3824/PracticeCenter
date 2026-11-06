-- Таблица отделов (с поддержкой иерархии)
CREATE TABLE departments (
    id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(150) NOT NULL,
    parent_id INT NULL,
    office_location VARCHAR(150) NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_departments_parent FOREIGN KEY (parent_id) REFERENCES departments(id)
);

-- Таблица сотрудников
CREATE TABLE users (
    id INT AUTO_INCREMENT PRIMARY KEY,
    department_id INT NOT NULL,
    login VARCHAR(100) NOT NULL UNIQUE,
    email VARCHAR(150) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL,
    first_name VARCHAR(100) NOT NULL,
    last_name VARCHAR(100) NOT NULL,
    middle_name VARCHAR(100) NULL,
    role ENUM('Employee', 'Manager', 'Administrator') NOT NULL DEFAULT 'Employee',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    failed_login_attempts INT NOT NULL DEFAULT 0,
    lockout_end DATETIME NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CONSTRAINT fk_users_department FOREIGN KEY (department_id) REFERENCES departments(id)
);

-- Таблица расчетных периодов (закрытие месяца)
CREATE TABLE accounting_periods (
    id INT AUTO_INCREMENT PRIMARY KEY,
    year_num INT NOT NULL,
    month_num INT NOT NULL,
    is_closed BOOLEAN NOT NULL DEFAULT FALSE,
    closed_at DATETIME NULL,
    closed_by INT NULL,
    CONSTRAINT fk_period_closed_by FOREIGN KEY (closed_by) REFERENCES users(id)
);

-- Таблица отсутствий (больничные, отпуска, отгулы)
CREATE TABLE absences (
    id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT NOT NULL,
    absence_date DATE NOT NULL,
    type ENUM('SickLeave', 'Vacation', 'DayOff', 'Truancy') NOT NULL,
    comment TEXT NULL,
    created_by INT NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_absences_user FOREIGN KEY (user_id) REFERENCES users(id),
    CONSTRAINT fk_absences_creator FOREIGN KEY (created_by) REFERENCES users(id)
);

-- Таблица рабочих смен
CREATE TABLE work_shifts (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    user_id INT NOT NULL,
    work_date DATE NOT NULL,
    start_time DATETIME NOT NULL,
    end_time DATETIME NULL,
    status ENUM('Working', 'OnBreak', 'Finished') NOT NULL DEFAULT 'Working',
    is_auto_closed BOOLEAN NOT NULL DEFAULT FALSE,
    net_work_minutes INT NOT NULL DEFAULT 0,
    report_text TEXT NULL,
    manager_comment TEXT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CONSTRAINT fk_shifts_user FOREIGN KEY (user_id) REFERENCES users(id)
);

-- Таблица перерывов смены
CREATE TABLE shift_breaks (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    work_shift_id BIGINT NOT NULL,
    start_time DATETIME NOT NULL,
    end_time DATETIME NULL,
    duration_minutes INT NOT NULL DEFAULT 0,
    CONSTRAINT fk_breaks_shift FOREIGN KEY (work_shift_id) REFERENCES work_shifts(id)
);

-- Таблица журнала аудита
CREATE TABLE audit_logs (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    changed_by INT NOT NULL,
    entity_name VARCHAR(100) NOT NULL,
    record_id BIGINT NOT NULL,
    action_type ENUM('Insert', 'Update', 'Delete', 'EmergencyAdjustment') NOT NULL,
    old_value_json JSON NULL,
    new_value_json JSON NULL,
    act_number VARCHAR(100) NULL,
    reason TEXT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_audit_user FOREIGN KEY (changed_by) REFERENCES users(id)
);

-- Таблица системных уведомлений (превышение лимита перерывов и др.)
CREATE TABLE system_notifications (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    manager_id INT NOT NULL,
    employee_id INT NOT NULL,
    message VARCHAR(255) NOT NULL,
    is_read BOOLEAN NOT NULL DEFAULT FALSE,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_notif_manager FOREIGN KEY (manager_id) REFERENCES users(id),
    CONSTRAINT fk_notif_employee FOREIGN KEY (employee_id) REFERENCES users(id)
);
