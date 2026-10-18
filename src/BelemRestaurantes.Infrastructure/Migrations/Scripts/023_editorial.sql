CREATE TABLE editorial_articles (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,title VARCHAR(160) NOT NULL,
 body TEXT NOT NULL,source_url VARCHAR(350) NULL,status VARCHAR(20) NOT NULL DEFAULT 'draft',
 version INT NOT NULL DEFAULT 1,updated_by VARCHAR(160) NOT NULL,updated_at DATETIME(6) NOT NULL,
 KEY ix_editorial_public(status,updated_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
