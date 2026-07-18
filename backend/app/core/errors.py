class DomainError(Exception):
    def __init__(self, status_code: int, code: str, message: str) -> None:
        self.status_code = status_code
        self.code = code
        self.message = message
        super().__init__(message)


def session_not_found() -> DomainError:
    return DomainError(404, "SESSION_NOT_FOUND", "Session was not found")


def session_expired() -> DomainError:
    return DomainError(410, "SESSION_EXPIRED", "Session has expired")


def session_completed() -> DomainError:
    return DomainError(409, "SESSION_COMPLETED", "Session no longer accepts actions")


def unknown_item() -> DomainError:
    return DomainError(422, "UNKNOWN_ITEM", "Item does not exist in the server catalog")


def action_id_conflict() -> DomainError:
    return DomainError(409, "ACTION_ID_CONFLICT", "action_id was reused with a different payload")


def item_instance_conflict() -> DomainError:
    return DomainError(409, "ITEM_INSTANCE_CONFLICT", "item_instance_id is already linked to another item_id")


def inventory_limit() -> DomainError:
    return DomainError(409, "INVENTORY_LIMIT", "Inventory cannot contain more than 50 items")


def log_limit_reached() -> DomainError:
    return DomainError(429, "LOG_LIMIT_REACHED", "Session action log limit was reached")


def result_timeout() -> DomainError:
    return DomainError(504, "RESULT_TIMEOUT", "Result generation is still in progress; retry this session")
