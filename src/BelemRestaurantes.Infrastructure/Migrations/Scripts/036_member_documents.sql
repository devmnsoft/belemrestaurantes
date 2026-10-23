-- Legacy accounts remain valid. No legacy rows are merged or assigned an invented document.
ALTER TABLE member_users
  ADD COLUMN cpf_digest CHAR(64) NULL,
  ADD COLUMN cpf_last4 CHAR(4) NULL,
  ADD UNIQUE KEY ux_member_cpf_digest (cpf_digest);

CREATE TABLE member_organizations (
  id CHAR(32) NOT NULL PRIMARY KEY,
  tax_digest CHAR(64) NOT NULL,
  tax_last4 CHAR(4) NOT NULL,
  legal_name VARCHAR(160) NOT NULL,
  created_at DATETIME(6) NOT NULL,
  verified_at DATETIME(6) NULL,
  UNIQUE KEY ux_member_organization_tax (tax_digest)
) ENGINE=InnoDB;

-- Declaring a company does not grant ownership, partner access or administrative privileges.
CREATE TABLE member_organization_requests (
  organization_id CHAR(32) NOT NULL,
  user_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  state VARCHAR(20) NOT NULL DEFAULT 'pending',
  created_at DATETIME(6) NOT NULL,
  PRIMARY KEY (organization_id,user_id),
  CONSTRAINT fk_org_request_org FOREIGN KEY (organization_id) REFERENCES member_organizations(id),
  CONSTRAINT fk_org_request_member FOREIGN KEY (user_id) REFERENCES member_users(id) ON DELETE CASCADE
) ENGINE=InnoDB;
