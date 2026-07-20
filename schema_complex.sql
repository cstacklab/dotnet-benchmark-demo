-- Users table
CREATE TABLE users (
    id SERIAL PRIMARY KEY,
    name TEXT NOT NULL,
    email TEXT NOT NULL,
    age INT NOT NULL,
    created_at TIMESTAMP DEFAULT now()
);

-- Orders table
CREATE TABLE orders (
    id SERIAL PRIMARY KEY,
    user_id INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    order_date TIMESTAMP DEFAULT now(),
    total_amount DECIMAL(10, 2) NOT NULL,
    status VARCHAR(20) DEFAULT 'pending'
);

-- Products table
CREATE TABLE products (
    id SERIAL PRIMARY KEY,
    name TEXT NOT NULL,
    price DECIMAL(10, 2) NOT NULL,
    category VARCHAR(100),
    stock INT DEFAULT 0
);

-- Order items (line items)
CREATE TABLE order_items (
    id SERIAL PRIMARY KEY,
    order_id INT NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    product_id INT NOT NULL REFERENCES products(id),
    quantity INT NOT NULL,
    unit_price DECIMAL(10, 2) NOT NULL
);

-- Create indexes for better query performance
CREATE INDEX idx_orders_user_id ON orders(user_id);
CREATE INDEX idx_order_items_order_id ON order_items(order_id);
CREATE INDEX idx_order_items_product_id ON order_items(product_id);
CREATE INDEX idx_users_age ON users(age);

-- Populate data
INSERT INTO users (id, name, email, age)
SELECT
    g,
    'User' || g,
    'user' || g || '@test.com',
    g % 80
FROM generate_series(1, 10000) g;

INSERT INTO products (name, price, category, stock)
SELECT
    'Product' || g,
    (RANDOM() * 1000)::DECIMAL,
    CASE (g % 5)
        WHEN 0 THEN 'Electronics'
        WHEN 1 THEN 'Clothing'
        WHEN 2 THEN 'Books'
        WHEN 3 THEN 'Home'
        ELSE 'Sports'
    END,
    (RANDOM() * 500)::INT
FROM generate_series(1, 500) g;

INSERT INTO orders (user_id, order_date, total_amount, status)
SELECT
    (RANDOM() * 9999 + 1)::INT,
    -- spread orders across 10 years (~40 quarters) so per-reporting-date grouping is meaningful
    TIMESTAMP '2016-01-01' + RANDOM() * (TIMESTAMP '2026-01-01' - TIMESTAMP '2016-01-01'),
    (RANDOM() * 5000)::DECIMAL,
    CASE (RANDOM() * 3)::INT
        WHEN 0 THEN 'pending'
        WHEN 1 THEN 'completed'
        ELSE 'cancelled'
    END
FROM generate_series(1, 50000) g;

INSERT INTO order_items (order_id, product_id, quantity, unit_price)
SELECT
    (RANDOM() * 49999 + 1)::INT,
    (RANDOM() * 499 + 1)::INT,
    (RANDOM() * 10 + 1)::INT,
    (RANDOM() * 1000)::DECIMAL
FROM generate_series(1, 150000) g;

